using System.Windows;

namespace Leaf.Controls.PlantAnimation;

/// <summary>
/// Defines the structural data for the plant: the stalk, its branches, and their leaves.
/// All geometry is defined relative to a coordinate system where the stalk base is at (0,0)
/// and growth is upward (negative Y in WPF screen coordinates).
/// </summary>
public class PlantModel
{
    public StalkDefinition Stalk { get; }
    public List<BranchDefinition> Branches { get; }

    public PlantModel(double stalkHeight, int branchCount)
    {
        var rng = new Random(42); // deterministic for consistency
        Stalk = CreateStalk(stalkHeight, rng);
        Branches = CreateBranches(stalkHeight, branchCount, rng);
    }

    private static StalkDefinition CreateStalk(double height, Random rng)
    {
        // Stalk as a cubic Bezier: slight natural S-curve
        var p0 = new Point(0, 0);
        var p1 = new Point(-3 + rng.NextDouble() * 6, -height * 0.33);
        var p2 = new Point(-3 + rng.NextDouble() * 6, -height * 0.66);
        var p3 = new Point(-1 + rng.NextDouble() * 2, -height);

        return new StalkDefinition(p0, p1, p2, p3, 7.0, 2.5);
    }

    private static List<BranchDefinition> CreateBranches(double stalkHeight, int branchCount, Random rng)
    {
        var branches = new List<BranchDefinition>();

        // Branches alternate left-right, evenly spaced along the stalk
        // First branch starts at ~20% height, last side branch at ~85%, top branch at ~95%
        int sideBranches = branchCount - 1; // last one is the top
        double startT = 0.20;
        double endT = 0.82;

        for (int i = 0; i < sideBranches; i++)
        {
            double t = startT + (endT - startT) * i / Math.Max(1, sideBranches - 1);
            bool isLeft = i % 2 == 0;

            // Branch angle from stalk: 35-55 degrees, slightly randomized
            double baseAngle = (35 + rng.NextDouble() * 20) * Math.PI / 180.0;
            double angle = isLeft ? (Math.PI / 2 + baseAngle) : (Math.PI / 2 - baseAngle);
            // In our coordinate system (Y negative up), left branches go to negative X
            // Adjust: left = toward negative X, right = toward positive X
            double dirX = isLeft ? -1 : 1;

            // Branch length scales with position — middle branches are longest
            double positionScale = Math.Sin(Math.PI * t); // peaks at 0.5
            double branchLength = (45 + rng.NextDouble() * 20) * (0.6 + 0.4 * positionScale);

            // Leaf size similarly scaled
            double leafLength = (55 + rng.NextDouble() * 15) * (0.55 + 0.45 * positionScale);
            double leafWidth = leafLength * (0.38 + rng.NextDouble() * 0.08);

            // Slight droop: branches curve downward slightly toward their tip
            double droop = (0.15 + rng.NextDouble() * 0.15) * branchLength;

            // Growth timing: lower branches appear first
            double growthDelay = t * 0.5; // 0..0.41 — staggered behind stalk growth

            branches.Add(new BranchDefinition
            {
                StalkParameter = t,
                IsLeft = isLeft,
                DirectionX = dirX,
                BranchAngle = baseAngle,
                BranchLength = branchLength,
                LeafLength = leafLength,
                LeafWidth = leafWidth,
                Droop = droop,
                GrowthDelay = growthDelay,
                NoiseOffset = i * 1.37 + rng.NextDouble(), // unique Perlin offset
                LeafColorVariation = (rng.NextDouble() - 0.5) * 0.6,
            });
        }

        // Top branch/leaf — grows straight up from the tip
        double topLeafLength = 40 + rng.NextDouble() * 10;
        branches.Add(new BranchDefinition
        {
            StalkParameter = 0.96,
            IsLeft = false,
            IsTop = true,
            DirectionX = 0,
            BranchAngle = 0,
            BranchLength = 0, // no branch, just a leaf
            LeafLength = topLeafLength,
            LeafWidth = topLeafLength * 0.40,
            Droop = 0,
            GrowthDelay = 0.50, // appears last
            NoiseOffset = branchCount * 1.37 + rng.NextDouble(),
            LeafColorVariation = (rng.NextDouble() - 0.5) * 0.4,
        });

        return branches;
    }
}

public record StalkDefinition(Point P0, Point P1, Point P2, Point P3, double BaseThickness, double TipThickness);

public class BranchDefinition
{
    public double StalkParameter { get; init; }  // 0..1 position along stalk
    public bool IsLeft { get; init; }
    public bool IsTop { get; init; }
    public double DirectionX { get; init; }       // -1 left, +1 right, 0 top
    public double BranchAngle { get; init; }      // radians from horizontal
    public double BranchLength { get; init; }
    public double LeafLength { get; init; }
    public double LeafWidth { get; init; }
    public double Droop { get; init; }
    public double GrowthDelay { get; init; }      // 0..1 delay before this branch starts growing
    public double NoiseOffset { get; init; }      // unique offset for Perlin wind sampling
    public double LeafColorVariation { get; init; }
}
