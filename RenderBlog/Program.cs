using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Scriban;
using Scriban.Runtime;
using SharpYaml.Serialization;

namespace RenderBlog
{
	public class Program
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

		static readonly CultureInfo DefaultCulture = new CultureInfo("en-US");

		public static void Main(string[] args)
		{
			CultureInfo.DefaultThreadCurrentCulture = DefaultCulture;

			var sitePath = args[0];
			var outputFolder = args[1];

			var siteConfiguration = ParseFrontMatter(File.ReadAllText(Path.Combine(sitePath, ConfigFile)));
			siteConfiguration.Add("time", DateTime.UtcNow);

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
				.ToList();

			var frontMatterFiles = files
				.Where(x => x.frontMatter != null)
				.ToList();
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
						frontMatter[TitleKey] = BetterTypography((string)frontMatter[TitleKey]);
					}
					var url = Path.GetExtension(localPath).Equals(HtmlExtension, StringComparison.Ordinal)
						? Path.ChangeExtension(localPath, null)
						: localPath;
					url = url.Replace(Path.DirectorySeparatorChar, UrlSeparator);
					url = url.Equals(IndexFile, StringComparison.Ordinal) || url.EndsWith($"{UrlSeparator}{IndexFile}", StringComparison.Ordinal)
						? url.Substring(0, Math.Max(url.Length - (IndexFile.Length + 1), 0))
						: url;
					url = url != string.Empty
						? $"{UrlSeparator}{url}"
						: $"{UrlSeparator}";
					frontMatter.Add("url", url);
					frontMatter.Add("TTR", TimeToRead(f.content));
					return (localPath: localPath, pageVariables: frontMatter, content: f.content, isMarkdown: isMarkdown, isPost: isPost);
				})
				.ToList();
			var posts = filesParsing.Where(x => x.isPost).ToList();
			var noPosts = filesParsing.Where(x => !x.isPost).ToList();
			var postsSorted = posts.OrderByDescending(x => x.pageVariables[DateKey]).ThenByDescending(x => x.localPath);
			siteConfiguration.Add("posts", postsSorted.Select(x => x.pageVariables).ToList());
			siteConfiguration.Add("html_pages", noPosts.Where(x => Path.GetExtension(x.localPath).Equals(HtmlExtension, StringComparison.Ordinal)).OrderBy(x => x.localPath).Select(x => x.pageVariables).ToList());
			var postsWithTags = postsSorted
				.Select(x => new
				{
					Post = x,
					Tags = (List<string>)x.pageVariables[TagsKey],
				})
				.ToList();
			siteConfiguration.Add(TagsKey, postsWithTags.SelectMany(x => x.Tags).Distinct().ToList());
			siteConfiguration.Add("years", posts.Select(x => (DateTime)x.pageVariables[DateKey]).Select(x => x.Year).OrderByDescending(x => x).Distinct().ToList());
			var postsWithYears = postsSorted
				.Select(x => new
				{
					Post = x,
					Year = ((DateTime)x.pageVariables[DateKey]).Year,
				})
				.ToList();
			var yearsWithPosts = new Dictionary<string, List<Dictionary<string, object>>>();
			foreach (var post in postsWithYears)
			{
				var year = post.Year.ToString();
				if (!yearsWithPosts.ContainsKey(year))
				{
					yearsWithPosts.Add(year, new List<Dictionary<string, object>>());
				}
				yearsWithPosts[year].Add(post.Post.pageVariables);
			}
			siteConfiguration.Add("years_posts", yearsWithPosts);
			siteConfiguration.Add("posts_by_id", posts.ToDictionary(x => x.pageVariables[IdKey].ToString(), x => x.pageVariables));

			Console.WriteLine(watch.Elapsed);
			watch.Restart();

			#endregion

			#region Local pre-rendering

			Console.Write("Pre-render:\t");

			posts.AsParallel().ForAll(item =>
			{
				var variables = new Dictionary<string, object>()
				{
					{ "site", siteConfiguration },
					{ "page", item.pageVariables },
				};

				var pageContent = Render(item.content, variables, sitePath);
				if (item.isMarkdown)
				{
					pageContent = MarkdownRenderer.RenderMarkdown(pageContent);
				}
				item.pageVariables["content"] = pageContent;
				item.pageVariables["excerpt"] = pageContent.Split(new[] { (string)siteConfiguration["excerpt_separator"] }, StringSplitOptions.None).First();
			});
			noPosts.AsParallel().ForAll(item =>
			{
				var variables = new Dictionary<string, object>()
				{
					{ "site", siteConfiguration },
					{ "page", item.pageVariables },
				};

				var pageContent = Render(item.content, variables, sitePath);
				if (item.isMarkdown)
				{
					pageContent = MarkdownRenderer.RenderMarkdown(pageContent);
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
					pageContent = Render(currentLayout.content, variables, sitePath);

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

		static void EnsureDirectoryExists(string file)
		{
			var dir = Path.GetDirectoryName(file);
			Directory.CreateDirectory(dir);
		}

		static string Render(string content, Dictionary<string, object> variables, string sitePath)
		{
			var template = Template.Parse(content);
			if (template.HasErrors)
			{
				throw new BlogException(string.Join(Environment.NewLine, template.Messages));
			}
			try
			{
				var templateContext = new TemplateContext();
				templateContext.BuiltinObject.Import(variables);
				var blogFunctions = new ScriptObject();
				blogFunctions.Import("file_hash", new Func<string, string>(FileHash));
				blogFunctions.Import("escape", new Func<string, string>(Escape));
				blogFunctions.Import("escape_list", new Func<IEnumerable<string>, IEnumerable<string>>(EscapeList));
				blogFunctions.Import("dt_string", new Func<DateTime, string, string>(DateTimeString));
				templateContext.BuiltinObject.SetValue("blog", blogFunctions, true);
				templateContext.TemplateLoader = new BlogTemplateLoader(Path.Combine(sitePath, IncludesFolder));
				templateContext.PushCulture(DefaultCulture);
				return template.Render(templateContext);
			}
			catch (Exception ex)
			{
				throw new BlogException(ex.Message, ex);
			}

			string FileHash(string filename)
			{
				var path = Path.Combine(sitePath, filename.TrimStart(UrlSeparator).Replace(UrlSeparator, Path.DirectorySeparatorChar));
				return Hash(path);

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

			static IEnumerable<string> EscapeList(IEnumerable<string> ss) => ss.Select(Escape);
			static string Escape(string s)
			{
				return s
					.Replace("&", "&amp;")
					.Replace("<", "&lt;")
					.Replace(">", "&gt;");
			}

			static string DateTimeString(DateTime dt, string format)
			{
				return dt.ToString(format);
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

		static int TimeToRead(string text)
		{
			var words = Regex.Matches(text, @"\w+").Count;
			var time = words / 180.0;
			if (time < 1)
				time = 1;
			return (int)Math.Round(time);
		}

		static string BetterTypography(string s)
		{
			s = s.Replace("'", "’");
			s = s.Replace("...", "…");
			s = s.Replace(" - ", " – ");
			s = Regex.Replace(s, @"""\b", "“");
			s = Regex.Replace(s, @"^""", "“");
			s = Regex.Replace(s, @"\b""", "”");
			s = Regex.Replace(s, @"""$", "”");
			return s;
		}

		public static class MarkdownRenderer
		{
			public static string RenderMarkdown(string content)
			{
				var builder = new MarkdownPipelineBuilder()
					.UseEmojiAndSmiley()
					.UseEmphasisExtras()
					.UsePipeTables();
				builder.DocumentProcessed += PostRenderMarkdown;
				var pipeline = builder.Build();
				using (var sw = new StringWriter())
				{
					var html = new HtmlRenderer(sw);
					html.ObjectRenderers.AddIfNotAlready<RawInlineRenderer>();
					Markdown.Convert(content, html, pipeline);
					sw.Flush();
					return sw.ToString();

				}
			}

			static void PostRenderMarkdown(MarkdownDocument document)
			{
				foreach (var item in document.Descendants())
				{
					switch (item)
					{
						case LiteralInline inline:
							{
								var newText = inline.ToString();
								inline.ReplaceBy(new RawInline(BetterTypography(newText)), true);
								break;
							}
						case HtmlInline inline:
							{
								var tag = inline.Tag;
								tag = Regex.Replace(tag, @"<(T[a-zA-Z0-9]*)>", "&lt;$1&gt;");
								inline.ReplaceBy(new RawInline(tag), true);
								break;
							}
						default:
							continue;
					}
				}
			}

			class RawInline : LeafInline
			{
				public string Text { get; }

				public RawInline(string text)
				{
					Text = text;
				}

			}

			class RawInlineRenderer : HtmlObjectRenderer<RawInline>
			{
				protected override void Write(HtmlRenderer renderer, RawInline obj)
				{
					renderer.Write(obj.Text);
				}
			}
		}

		class BlogTemplateLoader : ITemplateLoader
		{
			readonly string _includes;

			public BlogTemplateLoader(string includes)
			{
				_includes = includes;
			}

			public string GetPath(TemplateContext context, Scriban.Parsing.SourceSpan callerSpan, string templateName)
			{
				return Path.Combine(_includes, templateName);
			}

			public string Load(TemplateContext context, Scriban.Parsing.SourceSpan callerSpan, string templatePath)
			{
				return File.ReadAllText(templatePath);
			}

			public async ValueTask<string> LoadAsync(TemplateContext context, Scriban.Parsing.SourceSpan callerSpan, string templatePath)
			{
				return await File.ReadAllTextAsync(templatePath);
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