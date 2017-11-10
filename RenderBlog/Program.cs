﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DotLiquid;
using DotLiquid.FileSystems;
using Markdig;
using MyTypographyExtension;
using Newtonsoft.Json;
using SharpYaml.Serialization;

namespace RenderBlog
{
	class Program
	{
		const int BufferSize = 32 * 1024;
		const string FrontMatterSeparator = "---";
		const string ConfigFile = "_config.yml";
		const string LayoutsFolder = "_layouts";
		const string BaseLayout = "base.html";
		const string IncludesFolder = "_includes";
		const string PostsFolder = "_posts";
		const char UrlSeparator = '/';
		const string DateKey = "date";
		const string HtmlExtension = ".html";
		const string MarkdownExtension = ".md";
		const string IndexFile = "index";
		const string TagsKey = "tags";
		const string TitleKey = "title";
		const string IdKey = "id";

		static void Main(string[] args)
		{
			CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");

			var sitePath = args[0];
			var outputFolder = args[1];

			var siteConfiguration = ParseFrontMatter(File.ReadAllText(Path.Combine(sitePath, ConfigFile)));
			siteConfiguration.Add("time", DateTime.UtcNow);

			Template.FileSystem = new BlogFileSystem(Path.Combine(sitePath, IncludesFolder));
			Template.RegisterFilter(typeof(BlogFilters));
			Template.RegisterTagFactory(new BlogFileHashFactory(sitePath));

			var watch = Stopwatch.StartNew();

			#region Layouts loading

			Console.Write("Layouts:\t");

			var layouts = new Dictionary<string, (string path, string frontMatter, string content)>();
			var baseLayoutPath = Path.Combine(sitePath, LayoutsFolder, BaseLayout);
			layouts.Add(Path.GetFileNameWithoutExtension(BaseLayout), (baseLayoutPath, string.Empty, File.ReadAllText(baseLayoutPath)));
			foreach (var item in Directory.EnumerateFiles(Path.Combine(sitePath, LayoutsFolder)).Select(LoadFile).Where(x => x.frontMatter != null))
			{
				var key = Path.GetFileNameWithoutExtension(item.path);
				layouts.Add(key, item);
			}

			Console.WriteLine(watch.Elapsed);
			watch.Restart();

			#endregion

			#region Parsing

			Console.Write("Parsing:\t");

			var postsPath = Path.Combine(sitePath, PostsFolder);
			var files = EnumerateFiles(sitePath).Concat(EnumerateFiles(postsPath))
				.Select(LoadFile)
				.ToArray();

			var frontMatterFiles = files
				.Where(x => x.frontMatter != null)
				.ToArray();
			var filesParsing = frontMatterFiles.AsParallel()
				.Select(f =>
				{
					var isMarkdown = Path.GetExtension(f.path).Equals(MarkdownExtension, StringComparison.Ordinal);
					var isPost = Path.GetDirectoryName(f.path).Equals(postsPath, StringComparison.Ordinal);
					var localPath = isPost
						? f.path.Substring(sitePath.Length + 1 + PostsFolder.Length + 1)
						: f.path.Substring(sitePath.Length + 1);
					if (isMarkdown)
					{
						localPath = Path.ChangeExtension(localPath, HtmlExtension);
					}

					var frontMatter = ParseFrontMatter(f.frontMatter);
					frontMatter[IdKey] = isPost
						? GetPostId(Path.GetFileName(f.path))
						: null;
					if (frontMatter.ContainsKey(TagsKey))
					{
						frontMatter[TagsKey] = ((List<object>)frontMatter[TagsKey]).Cast<string>().ToList();
					}
					if (frontMatter.ContainsKey(TitleKey))
					{
						frontMatter[TitleKey] = Regex.Replace(RenderMarkdown((string)frontMatter[TitleKey]), @"<p>(.*)</p>\s*", "$1");
					}
					var url = Path.GetExtension(localPath).Equals(HtmlExtension, StringComparison.Ordinal)
						? Path.ChangeExtension(localPath, null)
						: localPath;
					url = url.Replace(Path.DirectorySeparatorChar, UrlSeparator);
					url = url.EndsWith(IndexFile, StringComparison.Ordinal)
						? url.Substring(0, Math.Max(url.Length - (IndexFile.Length + 1), 0))
						: url;
					url = url != string.Empty
						? $"{UrlSeparator}{url}{UrlSeparator}"
						: $"{UrlSeparator}";
					frontMatter.Add("url", url);
					return (localPath: localPath, pageVariables: frontMatter, content: f.content, isMarkdown: isMarkdown, isPost: isPost);
				})
				.ToArray();
			var posts = filesParsing.Where(x => x.isPost).ToArray();
			var postsSorted = posts.OrderByDescending(x => x.pageVariables[DateKey]).ThenByDescending(x => x.localPath);
			siteConfiguration.Add("posts", postsSorted.Select(x => x.pageVariables).ToList());
			siteConfiguration.Add("html_pages", filesParsing.Where(x => !x.isPost).Where(x => Path.GetExtension(x.localPath).Equals(HtmlExtension, StringComparison.Ordinal)).OrderBy(x => x.localPath).Select(x => x.pageVariables).ToList());
			var postsWithTags = postsSorted
				.Select(x => new
				{
					Post = x,
					Tags = (List<string>)x.pageVariables[TagsKey],
				})
				.ToArray();
			siteConfiguration.Add(TagsKey, postsWithTags.SelectMany(x => x.Tags).Distinct().ToList());
			siteConfiguration.Add("years", posts.Select(x => (DateTime)x.pageVariables[DateKey]).Select(x => x.Year).OrderByDescending(x => x).Distinct().ToList());
			var postsWithYears = postsSorted
				.Select(x => new
				{
					Post = x,
					Year = ((DateTime)x.pageVariables[DateKey]).Year,
				})
				.ToArray();
			var yearsWithPosts = new Dictionary<int, List<Dictionary<string, object>>>();
			foreach (var post in postsWithYears)
			{
				if (!yearsWithPosts.ContainsKey(post.Year))
				{
					yearsWithPosts.Add(post.Year, new List<Dictionary<string, object>>());
				}
				yearsWithPosts[post.Year].Add(post.Post.pageVariables);
			}
			siteConfiguration.Add("years_posts", yearsWithPosts);

			Console.WriteLine(watch.Elapsed);
			watch.Restart();

			#endregion

			#region Local pre-rendering

			Console.Write("Pre-render:\t");

			filesParsing.Where(x => x.isPost).AsParallel().ForAll(item =>
			{
				var variables = new Dictionary<string, object>()
				{
					{ "site", siteConfiguration },
					{ "page", item.pageVariables },
				};

				var pageContent = RenderLiquid(item.content, variables);
				if (item.isMarkdown)
				{
					pageContent = RenderMarkdown(pageContent);
				}
				item.pageVariables["content"] = pageContent;
				item.pageVariables["excerpt"] = pageContent.Split(new[] { (string)siteConfiguration["excerpt_separator"] }, StringSplitOptions.None).First();
			});
			filesParsing.Where(x => !x.isPost).AsParallel().ForAll(item =>
			{
				var variables = new Dictionary<string, object>()
				{
					{ "site", siteConfiguration },
					{ "page", item.pageVariables },
				};

				var pageContent = RenderLiquid(item.content, variables);
				if (item.isMarkdown)
				{
					pageContent = RenderMarkdown(pageContent);
				}
				item.pageVariables["content"] = pageContent;
				item.pageVariables["excerpt"] = pageContent.Split(new[] { (string)siteConfiguration["excerpt_separator"] }, StringSplitOptions.None).First();
			});

			Console.WriteLine(watch.Elapsed);
			watch.Restart();

			#endregion

			#region Full rendering

			Console.Write("Render:\t\t");

			filesParsing.AsParallel().ForAll(item =>
			{
				var variables = new Dictionary<string, object>()
				{
					{ "site", siteConfiguration },
					{ "page", item.pageVariables },
				};

				var layout = item.isPost ? "post" : string.Empty;
				var currentFrontMatter = item.pageVariables;
				var pageContent = (string)item.pageVariables["content"];
				while (true)
				{
					if (currentFrontMatter.TryGetValue("layout", out var layoutData))
					{
						layout = (string)layoutData;
					}
					if (layout == string.Empty)
						break;

					var currentLayout = layouts[layout];
					variables["content"] = pageContent;
					pageContent = RenderLiquid(currentLayout.content, variables);

					currentFrontMatter = ParseFrontMatter(currentLayout.frontMatter);

					if (currentLayout.path.Equals(baseLayoutPath, StringComparison.Ordinal))
						break;
				}

				var fileSystemPath = Path.Combine(outputFolder, item.localPath);
				EnsureDirectoryExists(fileSystemPath);
				File.WriteAllText(fileSystemPath, pageContent, new UTF8Encoding(false));
			});

			Console.WriteLine(watch.Elapsed);
			watch.Restart();

			#endregion

			#region Copy static files

			Console.Write("Statics:\t");

			foreach (var item in files.Where(x => x.frontMatter == null))
			{
				var localPath = item.path.Substring(sitePath.Length + 1);
				var fileSystemPath = Path.Combine(outputFolder, localPath);
				EnsureDirectoryExists(fileSystemPath);
				File.Copy(item.path, fileSystemPath, true);
			}

			Console.WriteLine(watch.Elapsed);
			watch.Restart();

			#endregion

			Console.WriteLine("Done");
		}

		static int? GetPostId(string filename)
		{
			var m = Regex.Match(filename, @"^(\d+)-.+");
			if (!m.Success)
				return null;
			return int.Parse(m.Groups[1].Value);
		}

		static object EnsureDirectoryExistsLock = new object();
		static void EnsureDirectoryExists(string file)
		{
			var dir = Path.GetDirectoryName(file);
			if (!Directory.Exists(dir))
			{
				lock (EnsureDirectoryExistsLock)
				{
					if (!Directory.Exists(dir))
					{
						Directory.CreateDirectory(dir);
					}
				}
			}
		}

		static string RenderMarkdown(string content)
		{
			var pipeline = new MarkdownPipelineBuilder()
				.UseEmojiAndSmiley()
				.UsePipeTables()
				.UseMyTypography()
				.Build();
			return Markdown.ToHtml(content, pipeline);
		}

		static string RenderLiquid(string content, Dictionary<string, object> variables)
		{
			var template = Template.Parse(content);
			template.MakeThreadSafe();
			if (template.Errors.Any())
			{
				throw new BlogException(string.Join(Environment.NewLine, template.Errors.Select(x => x.ToString())));
			}
			try
			{
				return template.Render(new RenderParameters()
				{
					LocalVariables = Hash.FromDictionary(variables),
					RethrowErrors = true,
				});
			}
			catch (Exception ex)
			{
				throw new BlogException(ex.Message, ex);
			}
		}

		static IEnumerable<string> EnumerateFiles(string path)
		{
			foreach (var item in Directory.EnumerateFiles(path).Where(FilterOutFileSystemItems))
			{
				yield return item;
			}
			foreach (var d in Directory.EnumerateDirectories(path).Where(FilterOutFileSystemItems))
			{
				foreach (var item in EnumerateFiles(d))
				{
					yield return item;
				}
			}
		}

		static bool FilterOutFileSystemItems(string path) =>
			!Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal)
			&&
			!Path.GetFileName(path).StartsWith("_", StringComparison.Ordinal);

		static (string path, string frontMatter, string content) LoadFile(string path)
		{
			var result = File.OpenRead(path);
			using (var reader = new StreamReader(result, Encoding.UTF8, false, BufferSize, true))
			{
				var line = reader.ReadLine();
				if (line.Equals(FrontMatterSeparator, StringComparison.Ordinal))
				{
					var frontMatter = new StringBuilder();
					while (!(line = reader.ReadLine()).Equals(FrontMatterSeparator, StringComparison.Ordinal))
					{
						frontMatter.AppendLine(line);
					}
					return (path, frontMatter.ToString(), reader.ReadToEnd());
				}
				else
				{
					return (path, null, null);
				}
			}
		}

		static Dictionary<string, object> ParseFrontMatter(string content)
		{
			var data = new Serializer().Deserialize<Dictionary<object, object>>(content);
			return data != null
				? RepackDictionary(data)
				: new Dictionary<string, object>();
		}

		static Dictionary<string, object> RepackDictionary(Dictionary<object, object> dictionary)
		{
			var result = new Dictionary<string, object>();
			foreach (var item in dictionary)
			{
				var key = (string)item.Key;
				switch (item.Value)
				{
					case Dictionary<object, object> d:
						result.Add(key, RepackDictionary(d));
						break;
					default:
						var value = item.Value;
						if (key.Equals(DateKey, StringComparison.Ordinal))
						{
							value = new DateTime(DateTime.ParseExact((string)value, "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", null).Ticks, DateTimeKind.Utc);
						}
						result.Add(key, value);
						break;
				}
			}
			return result;
		}

		class BlogFileSystem : IFileSystem
		{
			readonly string _includes;

			public BlogFileSystem(string includes)
			{
				_includes = includes;
			}

			public string ReadTemplateFile(Context context, string templateName)
			{
				return File.ReadAllText(Path.Combine(_includes, templateName));
			}
		}

		static class BlogFilters
		{
			public static string Jsonify(object input)
			{
				return JsonConvert.SerializeObject(input);
			}

			public static string Encode(string input)
			{
				// this is lame, but it's already partially encoded from MD's smarty pants and I want to keep it
				// so far it's mostly generics breaking the stuff
				return input
					.Replace("<", "&lt;")
					.Replace(">", "&gt;");
			}
		}

		class BlogFileHashFactory : ITagFactory
		{
			public string TagName => "file_hash";

			readonly string _sitePath;

			public BlogFileHashFactory(string sitePath)
			{
				_sitePath = sitePath;
			}

			public Tag Create()
			{
				return new BlogFileHash(_sitePath);
			}
		}
		class BlogFileHash : Tag
		{
			readonly string _sitePath;

			string _filename;

			public BlogFileHash(string sitePath)
			{
				_sitePath = sitePath;
			}

			public override void Initialize(string tagName, string markup, List<string> tokens)
			{
				base.Initialize(tagName, markup, tokens);
				_filename = markup.Trim();
			}

			public override void Render(Context context, TextWriter result)
			{
				base.Render(context, result);
				var path = Path.Combine(_sitePath, _filename.TrimStart(UrlSeparator).Replace(UrlSeparator, Path.DirectorySeparatorChar));
				result.Write(Hash(path));
			}

			static string Hash(string filename)
			{
				if (!File.Exists(filename))
					return string.Empty;
				using (var hash = HashAlgorithm.Create("SHA1"))
				{
					using (var fs = File.OpenRead(filename))
					{
						var hashBytes = hash.ComputeHash(fs);
						return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
					}
				}
			}
		}

		class BlogException : ArgumentException
		{
			public BlogException(string message)
				: base(message)
			{ }

			public BlogException(string message, Exception innerException)
				: base(message, innerException)
			{ }
		}
	}
}