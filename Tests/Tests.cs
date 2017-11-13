using NUnit.Framework;

namespace Tests
{
	[TestFixture]
	public class Tests
	{
		//[TestCase("hello' test", ExpectedResult = "<p>hello’ test</p>")]
		//[TestCase("hello test'", ExpectedResult = "<p>hello test’</p>")]
		//[TestCase("'hello test", ExpectedResult = "<p>’hello test</p>")]
		//[TestCase("hello 'test", ExpectedResult = "<p>hello ’test</p>")]
		//[TestCase("hello ' test", ExpectedResult = "<p>hello ’ test</p>")]
		//[TestCase("hel'lo test", ExpectedResult = "<p>hel’lo test</p>")]
		//[TestCase("...", ExpectedResult = "<p>…</p>")]
		//[TestCase("test...test", ExpectedResult = "<p>test…test</p>")]
		//[TestCase("aaa \"xxx\" bbb", ExpectedResult = "<p>aaa “xxx” bbb</p>")]
		//[TestCase("\"aaa\" \"bbb\"", ExpectedResult = "<p>“aaa” “bbb”</p>")]
		//[TestCase("\"\"", ExpectedResult = "<p>“”</p>")]
		//[TestCase("foo-bar", ExpectedResult = "<p>foo-bar</p>")]
		//[TestCase("foo - bar", ExpectedResult = "<p>foo – bar</p>")]
		//[TestCase("`hello' test`", ExpectedResult = "<p><code>hello' test</code></p>")]
		//[TestCase("```\nhello' test", ExpectedResult = "<pre><code>hello' test\n</code></pre>")]
		[TestCase(@"# h1
Test [foo][1]

[1]: http://example.com", ExpectedResult = "<h1>h1</h1>\n<p>Test <a href=\"http://example.com\">foo</a></p>")]
		public string Test(string input)
		{
			var result = RenderBlog.Program.MarkdownRenderer.RenderMarkdown(input);
			TestContext.WriteLine(result);
			return result.Remove(result.Length - 1, 1);
		}
	}
}
