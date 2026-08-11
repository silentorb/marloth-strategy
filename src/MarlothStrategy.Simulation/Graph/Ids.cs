namespace MarlothStrategy.Simulation.Graph;

public readonly record struct NodeId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct EdgeId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct NodeTypeId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct PortId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct SignalTypeId(string Value)
{
    public override string ToString() => Value;
}
