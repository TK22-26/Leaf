namespace Leaf.Controls.PlantAnimation;

/// <summary>
/// Damped spring physics for natural wind sway on individual plant elements.
/// Each leaf/branch gets its own instance with slightly different parameters.
/// </summary>
public class WindSpring
{
    private double _angle;
    private double _velocity;
    private readonly double _stiffness;
    private readonly double _damping;
    private readonly double _mass;

    public double Angle => _angle;

    public WindSpring(double stiffness = 35.0, double damping = 2.0, double mass = 1.0)
    {
        _stiffness = stiffness;
        _damping = damping;
        _mass = mass;
    }

    /// <summary>
    /// Reset the spring state so the animation starts fresh.
    /// </summary>
    public void Reset()
    {
        _angle = 0;
        _velocity = 0;
    }

    /// <summary>
    /// Advance the spring simulation by deltaTime seconds.
    /// Returns the current sway angle in radians.
    /// </summary>
    public double Update(double deltaTime, double windForce)
    {
        double springForce = -_stiffness * _angle;
        double dampingForce = -_damping * _velocity;
        double acceleration = (springForce + dampingForce + windForce) / _mass;

        // Semi-implicit Euler integration
        _velocity += acceleration * deltaTime;
        _angle += _velocity * deltaTime;

        return _angle;
    }
}

/// <summary>
/// Global wind field that uses Perlin noise to generate organic, spatially-coherent wind.
/// </summary>
public class WindField
{
    private double _baseStrength;
    private double _gustStrength;
    private double _gustFrequency;

    public double BaseStrength { get => _baseStrength; set => _baseStrength = value; }
    public double GustStrength { get => _gustStrength; set => _gustStrength = value; }

    public WindField(double baseStrength = 4.0, double gustStrength = 12.0, double gustFrequency = 0.3)
    {
        _baseStrength = baseStrength;
        _gustStrength = gustStrength;
        _gustFrequency = gustFrequency;
    }

    /// <summary>
    /// Sample the wind force at a given time and spatial position.
    /// heightRatio (0=base, 1=tip) increases wind effect higher up.
    /// uniqueOffset provides per-element variation so leaves don't all move in lockstep.
    /// </summary>
    public double Sample(double time, double heightRatio, double uniqueOffset)
    {
        // Base wind: slow, broad Perlin noise
        double base_ = PerlinNoise.FBM(time * 0.25, uniqueOffset * 0.5, octaves: 3) * _baseStrength;

        // Gusts: faster, sharper bursts
        double gust = PerlinNoise.FBM(time * _gustFrequency + 100, uniqueOffset * 0.3, octaves: 2) * _gustStrength;
        // Only apply gusts when they're positive (one-directional bursts)
        gust = Math.Max(0, gust);

        // Higher elements catch more wind
        double heightFactor = 0.3 + 0.7 * heightRatio;

        return (base_ + gust) * heightFactor;
    }
}
