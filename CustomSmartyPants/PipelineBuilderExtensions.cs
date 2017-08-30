using Markdig;

namespace CustomSmartyPants
{
	public static class PipelineBuilderExtensions
	{
		public static MarkdownPipelineBuilder UseCustomSmartyPants(this MarkdownPipelineBuilder builder)
		{
			return builder.Use<CustomSmartyPants.SmartyPants.SmartyPantsExtension>();
		}
	}
}
