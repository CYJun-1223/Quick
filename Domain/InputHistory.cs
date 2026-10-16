namespace Quick;

/// <summary>Linear list of launcher input states; Up/Down walks it, nothing is ever dropped.</summary>
internal sealed class InputHistory
{
    private readonly List<string> _states = [];
    private int _index = -1;

    /// <summary>Records <paramref name="state"/> right after the current one unless it is already current.</summary>
    public void Commit(string state)
    {
        if (string.IsNullOrWhiteSpace(state) || (_index >= 0 && _states[_index] == state))
        {
            return;
        }

        _states.Insert(++_index, state);
    }

    /// <summary>
    /// Moves one state back (<paramref name="delta"/> -1) or forward (+1) from <paramref name="current"/>.
    /// Text that is not the current state is kept as a new state first, so edits are never lost.
    /// </summary>
    public bool TryMove(string current, int delta, out string state)
    {
        state = string.Empty;
        if (string.IsNullOrWhiteSpace(current))
        {
            // Empty input: Up recalls the state we were last on.
            if (delta >= 0 || _index < 0)
            {
                return false;
            }

            state = _states[_index];
            return true;
        }

        var isDraft = _index < 0 || _states[_index] != current;
        if (isDraft)
        {
            if (delta > 0 || _states.Count == 0)
            {
                return false;
            }

            Commit(current);
        }

        var target = _index + delta;
        if (target < 0 || target >= _states.Count)
        {
            return false;
        }

        _index = target;
        state = _states[target];
        return true;
    }
}
