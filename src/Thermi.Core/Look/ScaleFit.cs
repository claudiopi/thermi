namespace Thermi.Core.Look;

/// <summary>
/// The scale at which an overlay fits a size, as its painter measures it: an overlay larger than its monitor's work area, or
/// an agent's picture past the longest side it gets.
/// </summary>
public static class ScaleFit
{
    // Text does not shrink quite in step with the scale, and rounding up may leave a pixel over, which another round takes
    // off; one more than ever needed, so a measure that never fits still ends.
    private const int Rounds = 4;

    /// <summary>
    /// <paramref name="scale"/>, or the smaller one at which <paramref name="measure"/> fits within <paramref name="most"/>,
    /// however small that is; after the last round, its scale as it stands.
    /// </summary>
    public static float Within(float scale, (int Width, int Height) most, Func<float, (int Width, int Height)> measure)
    {
        for (var round = 0; round < Rounds; round++)
        {
            var (width, height) = measure(scale);
            if (width <= most.Width && height <= most.Height)
            {
                break;
            }

            scale *= Math.Min((float)most.Width / width, (float)most.Height / height);
        }

        return scale;
    }
}
