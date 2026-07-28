global using ZLinq;
global using ZLinq.Linq;

// Same drop-in config as the production assemblies, so tests exercise the same binding.
[assembly: ZLinqDropIn("", DropInGenerateTypes.Collection)]
