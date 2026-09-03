using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// State가 독립적으로 생성한 InputActionAsset을 플레이어 GameObject의 Unity 수명주기에 연결한다.
// State 전환은 개별 State가 담당하고, 비활성화·재활성화·파괴 시의 안전한 정리는 이 컴포넌트가 담당한다.
public sealed class PlayerStateInputLifetime : MonoBehaviour
{
    private sealed class Entry
    {
        public IInputActionCollection2 Actions;
        public IDisposable Disposable;
        public Func<bool> ShouldBeEnabled;
        public Action BeforeDispose;
    }

    private readonly List<Entry> _entries = new();

    public void Register(
        IInputActionCollection2 actions,
        IDisposable disposable,
        Func<bool> shouldBeEnabled,
        Action beforeDispose
    )
    {
        if (actions == null)
            throw new ArgumentNullException(nameof(actions));
        if (disposable == null)
            throw new ArgumentNullException(nameof(disposable));
        if (shouldBeEnabled == null)
            throw new ArgumentNullException(nameof(shouldBeEnabled));

        _entries.Add(
            new Entry
            {
                Actions = actions,
                Disposable = disposable,
                ShouldBeEnabled = shouldBeEnabled,
                BeforeDispose = beforeDispose,
            }
        );
    }

    private void OnEnable()
    {
        Resume();
    }

    internal void Resume()
    {
        // GameObject나 Player 컴포넌트가 State 전환 없이 다시 활성화되면 현재 State의 입력만 복구한다.
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].ShouldBeEnabled())
                _entries[i].Actions.Enable();
        }
    }

    private void OnDisable()
    {
        Suspend();
    }

    internal void Suspend()
    {
        // Player 비활성화는 State.Exit을 거치지 않을 수 있으므로 모든 전용 입력을 즉시 중단한다.
        for (int i = 0; i < _entries.Count; i++)
            _entries[i].Actions.Disable();
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            Entry entry = _entries[i];
            entry.Actions.Disable();
            entry.BeforeDispose?.Invoke();
            entry.Disposable.Dispose();
        }

        _entries.Clear();
    }
}
