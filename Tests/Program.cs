using System.Reflection;
using NUnitLite;

namespace Tests
{
	class Program
	{
		static int Main(string[] args)
		{
			return new AutoRun(Assembly.GetExecutingAssembly()).Execute(new[] { "--noresult" });
		}
	}
}
