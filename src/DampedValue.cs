using System;

namespace GlassBongoCat
{
    // Analytic critical damping for the material's small squash and drag offset.
    // No numerical substeps, overshoot, or dependency on the optics renderer.
    internal sealed class DampedValue
    {
        internal double Value, Velocity, Target;
        internal void Reset() { Value = Velocity = Target = 0; }
        internal void Advance(double dt)
        {
            dt = Math.Max(0, Math.Min(.1, dt));
            const double frequency = 20;
            double displacement = Value - Target;
            double impulse = Velocity + frequency * displacement;
            double decay = Math.Exp(-frequency * dt);
            Value = Target + (displacement + impulse * dt) * decay;
            Velocity = (Velocity - frequency * impulse * dt) * decay;
            if (Math.Abs(Value - Target) < .0001 && Math.Abs(Velocity) < .001)
            { Value = Target; Velocity = 0; }
        }
    }
}
