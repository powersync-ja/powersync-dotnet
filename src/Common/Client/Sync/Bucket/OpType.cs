namespace Common.Client.Sync.Bucket;
using System;

public enum OpTypeEnum
{
    CLEAR = 1,
    MOVE = 2,
    PUT = 3,
    REMOVE = 4
}

public class OpType(OpTypeEnum value)
{
    public OpTypeEnum Value { get; } = value;

    public static OpType FromJSON(string jsonValue)
    {
        if (Enum.TryParse<OpTypeEnum>(jsonValue, out var enumValue))
        {
            return new OpType(enumValue);
        }
        throw new ArgumentException($"Invalid JSON value for OpTypeEnum: {jsonValue}");
    }

    public string ToJSON()
    {
        return Value.ToString();
    }
}