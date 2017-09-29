using Markdig;

namespace MyTypographyExtension
{
	public static class PipelineBuilderExtensions
	{
		public static MarkdownPipelineBuilder UseMyTypography(this MarkdownPipelineBuilder builder)
		{
			return builder.Use<MyTypographyExtension>();
		}
	}
}
