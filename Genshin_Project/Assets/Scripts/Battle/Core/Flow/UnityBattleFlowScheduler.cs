using System;
using System.Collections;
using UnityEngine;

/// <summary>Production scheduler backed by one coroutine owned by a MonoBehaviour.</summary>
public sealed class UnityBattleFlowScheduler : IBattleFlowScheduler
{
    private readonly MonoBehaviour _owner;
    private Coroutine _coroutine;

    public bool IsRunning { get; private set; }

    public UnityBattleFlowScheduler(MonoBehaviour owner)
    {
        _owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
    }

    public void Start(IEnumerator routine)
    {
        if (routine == null) throw new ArgumentNullException(nameof(routine));
        if (IsRunning) throw new InvalidOperationException("The battle flow scheduler is already running.");

        IsRunning = true;
        _coroutine = _owner.StartCoroutine(Run(routine));
        // StartCoroutine may complete a routine synchronously before returning its handle.
        if (!IsRunning) _coroutine = null;
    }

    public void Stop()
    {
        Coroutine coroutine = _coroutine;
        _coroutine = null;
        IsRunning = false;
        if (coroutine != null && _owner != null)
            _owner.StopCoroutine(coroutine);
    }

    private IEnumerator Run(IEnumerator routine)
    {
        try
        {
            yield return routine;
        }
        finally
        {
            _coroutine = null;
            IsRunning = false;
        }
    }
}
