// Compile-check shims: engine types that exist in Unity 6 but not in the 2021.3 reference
// assemblies used for compiling here. Never shipped to Unity.
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public class UGUIHelpURLAttribute : Attribute { public UGUIHelpURLAttribute(string pageName) { } }

    public enum PenStatus { None = 0, Contact = 1, Barrel = 2, Inverted = 4, Eraser = 8 }

    [AsyncMethodBuilder(typeof(AwaitableBuilder))]
    public class Awaitable
    {
        public static Awaitable NextFrameAsync(CancellationToken cancellationToken = default) => new Awaitable();
        public Awaiter GetAwaiter() => new Awaiter();
        public struct Awaiter : INotifyCompletion
        {
            public bool IsCompleted => true;
            public void GetResult() { }
            public void OnCompleted(Action continuation) => continuation();
        }
    }
}

namespace UnityEngine
{
    public struct AwaitableBuilder
    {
        public static AwaitableBuilder Create() => default;
        public Awaitable Task => new Awaitable();
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
        public void SetResult() { }
        public void SetException(Exception exception) { }
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine { }
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine { }
    }
}

namespace Unity.Scripting.LifecycleManagement
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class NoAutoStaticsCleanupAttribute : Attribute { }
}
