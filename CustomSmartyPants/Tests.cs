using Markdig;
using NUnit.Framework;

namespace CustomSmartyPants
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
		[TestCase("\"hello' test\"", ExpectedResult = "<p>&ldquo;hello&rsquo; test&rdquo;</p>")]
		[TestCase("\"hello test'\"", ExpectedResult = "<p>&ldquo;hello test&rsquo;&rdquo;</p>")]
		[TestCase("\"'hello test\"", ExpectedResult = "<p>&ldquo;&rsquo;hello test&rdquo;</p>")]
		[TestCase("\"hello 'test\"", ExpectedResult = "<p>&ldquo;hello &rsquo;test&rdquo;</p>")]
		[TestCase("\"hello ' test\"", ExpectedResult = "<p>&ldquo;hello &rsquo; test&rdquo;</p>")]
		[TestCase("'hello test'", ExpectedResult = "<p>&lsquo;hello test&rsquo;</p>")]
		[TestCase("'hello\" test'", ExpectedResult = "<p>&lsquo;hello&quot; test&rsquo;</p>")]
		[TestCase("'hello' test'", ExpectedResult = "<p>&lsquo;hello&rsquo; test&rsquo;</p>")]
		[TestCase("'hello 'test'", ExpectedResult = "<p>&rsquo;hello &lsquo;test&rsquo;</p>")]
		public string Test(string input)
		{
			var pipeline = new MarkdownPipelineBuilder()
				.UseCustomSmartyPants()
				.Build();
			var result = Markdown.ToHtml(input, pipeline);
			return result.Remove(result.Length - 1, 1);
		}
	}
}
