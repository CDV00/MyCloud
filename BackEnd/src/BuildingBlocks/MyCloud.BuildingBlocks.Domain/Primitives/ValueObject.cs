namespace MyCloud.BuildingBlocks.Domain.Primitives;

public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetAtomicValues();

    public bool Equals(ValueObject? other) => other is not null && ValuesAreEqual(other);

    public override bool Equals(object? obj) => obj is ValueObject other && ValuesAreEqual(other);

    public override int GetHashCode() =>
        GetAtomicValues().Aggregate(0, (hash, value) => HashCode.Combine(hash, value));

    public static bool operator ==(ValueObject? left, ValueObject? right) => Equals(left, right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !Equals(left, right);

    private bool ValuesAreEqual(ValueObject other) =>
        GetType() == other.GetType() && GetAtomicValues().SequenceEqual(other.GetAtomicValues());
}
