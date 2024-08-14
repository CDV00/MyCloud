using System.Reflection;

namespace MyDrive.BuidingBlock.Contract;

public class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
