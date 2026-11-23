// Offline test runner: dotnet offline-runner.dll <test-assembly.dll> [filter-substring]
using System.Reflection;
using System.Runtime.Loader;
using Xunit;

var path = Path.GetFullPath(args[0]);
var filter = args.Length > 1 ? args[1] : null;
var dir = Path.GetDirectoryName(path)!;
AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    var p = Path.Combine(dir, name.Name + ".dll");
    return name.Name == "Xunit.Shim" ? typeof(Assert).Assembly : File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
};
var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
int pass = 0, fail = 0, skip = 0;
foreach (var type in asm.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.IsPublic).OrderBy(t => t.FullName))
foreach (var m in type.GetMethods().Where(m => m.GetCustomAttribute<FactAttribute>() is not null).OrderBy(m => m.Name))
{
    var name = $"{type.FullName}.{m.Name}";
    if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
    var fact = m.GetCustomAttribute<FactAttribute>()!;
    if (fact.Skip is not null) { skip++; Console.WriteLine($"  SKIP {name}: {fact.Skip}"); continue; }
    var rows = m.GetCustomAttributes<InlineDataAttribute>().Select(a => a.Data).ToList();
    if (rows.Count == 0) rows.Add([]);
    foreach (var row in rows)
    {
        var label = row.Length == 0 ? name : $"{name}({string.Join(", ", row)})";
        object? inst = null;
        try
        {
            inst = Activator.CreateInstance(type);
            if (inst is IAsyncLifetime al) await al.InitializeAsync();
            var ps = m.GetParameters();
            var argv = row.Select((v, i) => v is null || ps[i].ParameterType.IsInstanceOfType(v) ? v : Convert.ChangeType(v, Nullable.GetUnderlyingType(ps[i].ParameterType) ?? ps[i].ParameterType)).ToArray();
            var r = m.Invoke(inst, argv);
            if (r is Task t) await t;
            pass++;
        }
        catch (Exception e)
        {
            if (e is TargetInvocationException { InnerException: { } ie }) e = ie;
            fail++;
            Console.WriteLine($"  FAIL {label}\n       {e.GetType().Name}: {e.Message}\n{string.Join("\n", (e.StackTrace ?? "").Split('\n').Take(4))}");
        }
        finally
        {
            if (inst is IAsyncLifetime al2) await al2.DisposeAsync();
            (inst as IDisposable)?.Dispose();
        }
    }
}
Console.WriteLine($"Passed: {pass}, Failed: {fail}, Skipped: {skip}, Total: {pass + fail + skip}");
return fail == 0 && pass > 0 ? 0 : 1;
