using NUnitLite;

namespace Tests
{
	class Program
	{
		static int Main(string[] args)
		{
			return new AutoRun().Execute(new[] { "--noresult" });
		}
	}
}
