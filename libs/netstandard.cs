[assembly: System.Reflection.AssemblyVersion("2.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.1.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("2.1.0.0")]
[assembly: System.Runtime.CompilerServices.ReferenceAssembly]

namespace System
{
    public ref struct ReadOnlySpan<T>
    {
        public ReadOnlySpan(T[] array) { }
    }

    public ref struct Span<T>
    {
        public Span(T[] array) { }
    }
}
