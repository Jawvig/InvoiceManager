// The `union` language feature relies on two support types that ship in the net11.0 BCL
// (System.Runtime.CompilerServices.IUnion / UnionAttribute). They do not exist in net10.0,
// so when this project is built for its net10.0 target (deployed to Azure Functions Flex
// Consumption, which has no net11.0 stack) we polyfill them here — the same pattern used for
// IsExternalInit on older frameworks. The net11.0 build excludes this file and uses the real
// BCL types, so there is no conflict.
//
// Signatures mirror the net11.0 reference assembly exactly:
//   public interface IUnion { object? Value { get; } }
//   [AttributeUsage(Class | Struct, AllowMultiple = false, Inherited = false)]
//   public sealed class UnionAttribute : Attribute { public UnionAttribute(); }
#if NET10_0

namespace System.Runtime.CompilerServices
{
    public interface IUnion
    {
        object? Value { get; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class UnionAttribute : Attribute
    {
        public UnionAttribute()
        {
        }
    }
}

#endif
