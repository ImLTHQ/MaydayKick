using System;
using System.Collections.Concurrent;
using System.Threading;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace RoomKit
{
    internal static class MainThreadActionRunner
    {
        private static readonly ConcurrentQueue<Action> Actions = new ConcurrentQueue<Action>();
        private static bool initialized;

        internal static void Initialize()
        {
            if (Volatile.Read(ref initialized)) return;

            ClassInjector.RegisterTypeInIl2Cpp<RoomKitMainThreadRunner>();
            var gameObject = new GameObject("RoomKit.MainThreadActionRunner");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<RoomKitMainThreadRunner>();
            Volatile.Write(ref initialized, true);
        }

        internal static bool Enqueue(Action action)
        {
            if (!Volatile.Read(ref initialized) || action == null) return false;
            Actions.Enqueue(action);
            return true;
        }

        internal static void RunPending()
        {
            Action action;
            while (Actions.TryDequeue(out action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError("[RoomKit] 主线程异步任务执行失败: " + e);
                }
            }
        }
    }

    public sealed class RoomKitMainThreadRunner : MonoBehaviour
    {
        public RoomKitMainThreadRunner(IntPtr pointer) : base(pointer) { }

        private void Update()
        {
            MainThreadActionRunner.RunPending();
        }
    }
}
