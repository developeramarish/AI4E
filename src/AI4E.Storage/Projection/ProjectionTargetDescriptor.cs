using System;

namespace AI4E.Storage.Projection
{
    internal readonly struct ProjectionTargetDescriptor : IEquatable<ProjectionTargetDescriptor>
    {
        public ProjectionTargetDescriptor(Type targetType, string targetId)
        {
            if (targetType == null)
                throw new ArgumentNullException(nameof(targetType));

            if (targetId == null || targetId.Equals(default))
                throw new ArgumentDefaultException(nameof(targetId));

            TargetType = targetType;
            TargetId = targetId;
        }

        public Type TargetType { get; }
        public string TargetId { get; }

        public override bool Equals(object obj)
        {
            return obj is ProjectionTargetDescriptor entityDescriptor && Equals(entityDescriptor);
        }

        public bool Equals(ProjectionTargetDescriptor other)
        {
            return other.TargetType == null && TargetType == null || other.TargetType == TargetType && other.TargetId.Equals(TargetId);
        }

        public override int GetHashCode()
        {
            if (TargetType == null)
                return 0;

            return TargetType.GetHashCode() ^ TargetId.GetHashCode();
        }

        public static bool operator ==(in ProjectionTargetDescriptor left, in ProjectionTargetDescriptor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(in ProjectionTargetDescriptor left, in ProjectionTargetDescriptor right)
        {
            return !left.Equals(right);
        }
    }

    internal readonly struct ProjectionTargetDescriptor<TId> : IEquatable<ProjectionTargetDescriptor<TId>>
    {
        public ProjectionTargetDescriptor(Type targetType, TId targetId)
        {
            if (targetType == null)
                throw new ArgumentNullException(nameof(targetType));

            if (targetId == null || targetId.Equals(default))
                throw new ArgumentDefaultException(nameof(targetId));

            TargetType = targetType;
            TargetId = targetId;
        }

        public Type TargetType { get; }
        public TId TargetId { get; }
        public string StringifiedTargetId => TargetId.ToString();

        public override bool Equals(object obj)
        {
            return obj is ProjectionTargetDescriptor<TId> entityDescriptor && Equals(entityDescriptor);
        }

        public bool Equals(ProjectionTargetDescriptor<TId> other)
        {
            return other.TargetType == null && TargetType == null || other.TargetType == TargetType && other.TargetId.Equals(TargetId);
        }

        public override int GetHashCode()
        {
            if (TargetType == null)
                return 0;

            return TargetType.GetHashCode() ^ TargetId.GetHashCode();
        }

        public static bool operator ==(in ProjectionTargetDescriptor<TId> left, in ProjectionTargetDescriptor<TId> right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(in ProjectionTargetDescriptor<TId> left, in ProjectionTargetDescriptor<TId> right)
        {
            return !left.Equals(right);
        }

        public static implicit operator ProjectionTargetDescriptor(in ProjectionTargetDescriptor<TId> typedDescriptor)
        {
            return new ProjectionTargetDescriptor(typedDescriptor.TargetType, typedDescriptor.StringifiedTargetId);
        }
    }
}
