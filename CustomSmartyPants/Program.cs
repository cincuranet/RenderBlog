using NUnitLite;

namespace CustomSmartyPants
{
    class Program
    {
        static int Main()
        {
            return new AutoRun().Execute(new[] { "--noresult" });
        }
    }
}