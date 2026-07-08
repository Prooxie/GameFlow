namespace GameFlow.Core.Models;

public readonly record struct StickVector(float X, float Y)
{
    public static StickVector Zero => new(0f, 0f);

    public bool IsNearZero(float epsilon = 0.0001f)
    {
        return MathF.Abs(X) < epsilon && MathF.Abs(Y) < epsilon;
    }

    public StickVector Clamp()
    {
        return new(Math.Clamp(X, -1f, 1f), Math.Clamp(Y, -1f, 1f));
    }

    public StickVector WithDeadzone(float deadzone)
    {
        var x = MathF.Abs(X) < deadzone ? 0f : X;
        var y = MathF.Abs(Y) < deadzone ? 0f : Y;
        return new StickVector(x, y);
    }

    public StickVector AmplifyToFull(float fullAt)
    {
        if (fullAt <= 0f)
        {
            return Clamp();
        }

        static float Scale(float value, float fullAtValue)
        {
            var absolute = MathF.Abs(value);
            if (absolute < 0.000001f)
            {
                return 0f;
            }

            var scaled = MathF.Min(absolute / fullAtValue, 1f);
            return value >= 0f ? scaled : -scaled;
        }

        return new StickVector(Scale(X, fullAt), Scale(Y, fullAt)).Clamp();
    }

    public static StickVector operator +(StickVector left, StickVector right)
    {
        return new(left.X + right.X, left.Y + right.Y);
    }

    public override string ToString()
    {
        return $"({X:0.000}, {Y:0.000})";
    }
}
