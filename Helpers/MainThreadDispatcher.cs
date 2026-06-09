using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Mcp.Helpers
{
    public sealed class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher instance;
        private static readonly Queue<Action> Actions = new Queue<Action>();

        public static void Ensure()
        {
            if (instance != null)
            {
                return;
            }

            var go = new GameObject("MCP_MainThreadDispatcher");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<MainThreadDispatcher>();
        }

        public static Task<T> Run<T>(Func<T> work)
        {
            Ensure();
            var completion = new TaskCompletionSource<T>();
            lock (Actions)
            {
                Actions.Enqueue(() =>
                {
                    try
                    {
                        completion.SetResult(work());
                    }
                    catch (Exception ex)
                    {
                        completion.SetException(ex);
                    }
                });
            }

            return completion.Task;
        }

        private void Update()
        {
            while (true)
            {
                Action action;
                lock (Actions)
                {
                    if (Actions.Count == 0)
                    {
                        return;
                    }

                    action = Actions.Dequeue();
                }

                action();
            }
        }
    }
}
