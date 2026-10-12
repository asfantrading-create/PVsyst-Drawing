using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PVDesigner.App
{
    /// <summary>
    /// Runs heavy calculations (shading, simulation) on worker threads and
    /// delivers results back on the Unity main thread.
    /// </summary>
    public class JobRunner : MonoBehaviour
    {
        private sealed class ProgressSink : IProgress<float>
        {
            public volatile float Value;
            public void Report(float value) => Value = value;
        }

        private Task _task;
        private Action _complete;
        private ProgressSink _progress;
        private CancellationTokenSource _cts;

        public bool IsBusy => _task != null;
        public string CurrentJob { get; private set; } = "";
        public float Progress => _progress?.Value ?? 0f;

        public event Action<string> JobStarted;
        public event Action<string> JobFinished;

        /// <summary>Starts a job. Returns false if another job is running.</summary>
        public bool Run<T>(string name, Func<IProgress<float>, CancellationToken, T> work, Action<T> onSuccess, Action<Exception> onError)
        {
            if (IsBusy) return false;
            CurrentJob = name;
            _progress = new ProgressSink();
            _cts = new CancellationTokenSource();
            var progress = _progress;
            var token = _cts.Token;
            var task = Task.Run(() => work(progress, token), token);
            _task = task;
            _complete = () =>
            {
                if (task.IsCanceled)
                    onError?.Invoke(new OperationCanceledException("Cancelled"));
                else if (task.IsFaulted)
                    onError?.Invoke(task.Exception?.GetBaseException() ?? new Exception("Unknown error"));
                else
                    onSuccess?.Invoke(task.Result);
            };
            JobStarted?.Invoke(name);
            return true;
        }

        public void Cancel() => _cts?.Cancel();

        private void Update()
        {
            if (_task == null || !_task.IsCompleted) return;
            var complete = _complete;
            string name = CurrentJob;
            _task = null;
            _complete = null;
            _cts?.Dispose();
            _cts = null;
            try
            {
                complete?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            CurrentJob = "";
            JobFinished?.Invoke(name);
        }

        private void OnDestroy() => _cts?.Cancel();
    }
}
