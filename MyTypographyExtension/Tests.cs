using Markdig;
using NUnit.Framework;

namespace MyTypographyExtension
{
	[TestFixture]
	public class Tests
	{
		[TestCase("hello' test", ExpectedResult = "<p>hello&rsquo; test</p>")]
		[TestCase("hello test'", ExpectedResult = "<p>hello test&rsquo;</p>")]
		[TestCase("'hello test", ExpectedResult = "<p>&rsquo;hello test</p>")]
		[TestCase("hello 'test", ExpectedResult = "<p>hello &rsquo;test</p>")]
		[TestCase("hello ' test", ExpectedResult = "<p>hello &rsquo; test</p>")]
		[TestCase("hel'lo test", ExpectedResult = "<p>hel&rsquo;lo test</p>")]
		[TestCase("...", ExpectedResult = "<p>&hellip;</p>")]
		public string Test(string input)
		{
			var pipeline = new MarkdownPipelineBuilder()
				.UseMyTypography()
				.Build();
			var result = Markdown.ToHtml(input, pipeline);
			return result.Remove(result.Length - 1, 1);
		}
	}
}
