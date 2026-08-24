using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>Deterministically executes Unity-style nested IEnumerator flows in EditMode.</summary>
    internal sealed class ManualBattleFlowScheduler : IBattleFlowScheduler
    {
        private readonly Stack<IEnumerator> _routines = new Stack<IEnumerator>();

        public bool IsRunning => _routines.Count > 0;

        public void Start(IEnumerator routine)
        {
            if (routine == null) throw new ArgumentNullException(nameof(routine));
            if (IsRunning) throw new InvalidOperationException("The battle flow scheduler is already running.");
            _routines.Push(routine);
        }

        public void Stop()
        {
            while (_routines.Count > 0)
            {
                IEnumerator routine = _routines.Pop();
                if (routine is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        /// <summary>
        /// Runs until the next null/WaitForSeconds scheduling boundary or natural completion.
        /// Returns whether a root flow remains scheduled.
        /// </summary>
        public bool Step()
        {
            if (!IsRunning) return false;

            try
            {
                while (_routines.Count > 0)
                {
                    IEnumerator current = _routines.Peek();
                    if (!current.MoveNext())
                    {
                        _routines.Pop();
                        if (current is IDisposable disposable)
                            disposable.Dispose();
                        continue;
                    }

                    object yielded = current.Current;
                    if (yielded == null || yielded is WaitForSeconds)
                        return true;

                    if (yielded is IEnumerator nested)
                    {
                        _routines.Push(nested);
                        continue;
                    }

                    throw new NotSupportedException(
                        $"Manual battle flow scheduler does not support yielded object type '{yielded.GetType().FullName}'.");
                }

                return false;
            }
            catch
            {
                Stop();
                throw;
            }
        }
    }
}
