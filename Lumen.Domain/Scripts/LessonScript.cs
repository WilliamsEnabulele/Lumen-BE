namespace Lumen.Domain.Scripts;

/// <summary>
/// A lesson's nodes in teaching order, with the navigation the session engine needs.
///
/// Ordering happens once, here, on construction. A script whose order depends on how the
/// rows came back from a query is a script that can teach two students the same lesson
/// differently.
/// </summary>
public sealed class LessonScript
{
    private readonly List<ScriptNode> _nodes;
    private readonly Dictionary<Guid, int> _positions;

    public LessonScript(Guid lessonId, IEnumerable<ScriptNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        LessonId = lessonId;
        _nodes = nodes.OrderBy(node => node.Ordinal).ToList();

        if (_nodes.Count == 0)
            throw new ArgumentException("A lesson script with no nodes cannot be taught.", nameof(nodes));

        _positions = new Dictionary<Guid, int>(_nodes.Count);
        for (var i = 0; i < _nodes.Count; i++)
        {
            if (!_positions.TryAdd(_nodes[i].Id, i))
                throw new ArgumentException($"Script node {_nodes[i].Id} appears twice.", nameof(nodes));
        }
    }

    public Guid LessonId { get; }

    public IReadOnlyList<ScriptNode> Nodes => _nodes;

    public ScriptNode First => _nodes[0];

    public ScriptNode? Find(Guid scriptNodeId) =>
        _positions.TryGetValue(scriptNodeId, out var position) ? _nodes[position] : null;

    public ScriptNode? Next(Guid scriptNodeId)
    {
        if (!_positions.TryGetValue(scriptNodeId, out var position)) return null;
        return position + 1 < _nodes.Count ? _nodes[position + 1] : null;
    }

    public bool IsLast(Guid scriptNodeId) =>
        _positions.TryGetValue(scriptNodeId, out var position) && position == _nodes.Count - 1;
}
