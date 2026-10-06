namespace Prognode.Contracts.Licensing;

public readonly record struct CapacityLimit(int? Value)
{
    public bool IsUnlimited => Value is null;

    public static CapacityLimit Unlimited => new(null);

    public static CapacityLimit Limited(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        return new CapacityLimit(value);
    }

    public override string ToString() =>
        IsUnlimited ? "UNLIMITED" : Value!.Value.ToString();
}
