using Unity.Mathematics;
using UnityEngine.Jobs;


public static class MathematicsUtil
{
    public const float PI = 3.14159265f;
    public const float kEpsilon = 0.000001F;

    // Degrees-to-radians conversion constant (RO).
    public const float Deg2Rad = PI * 2F / 360F;

    // Radians-to-degrees conversion constant (RO).
    public const float Rad2Deg = 1F / Deg2Rad;

    public static readonly float3 right = new float3(1, 0, 0);
    public static readonly float3 up = new float3(0, 1, 0);
    public static readonly float3 forward = new float3(0, 0, 1);
    public static readonly float3 one = new float3(1, 1, 1);

    public static float3 LocalToWorldPosition(float3 parentPosition, quaternion parentRotation, float3 targetLocalPosition)
    {
        return parentPosition + math.mul(parentRotation, targetLocalPosition);
    }

    public static quaternion LocalToWorldRotation(quaternion parentRotation, quaternion targetLocalRotation)
    {
        return math.mul(parentRotation, targetLocalRotation);
    }

    public static float3 WorldToLocalPosition(float3 parentPosition, quaternion parentRotation, float3 targetWorldPosition)
    {
        return float3.zero;
    }

    public static quaternion WorldToLocalRotation(quaternion parentRotation, quaternion targetWorldRotation)
    {
        return quaternion.identity;
    }

    public static float3 GetMatrixPosition(float4x4 matrix)
    {
        return matrix.c3.xyz;
    }

    public static quaternion GetMatrixRotation(float4x4 matrix)
    {
        return quaternion.LookRotation(matrix.c2.xyz, matrix.c1.xyz);
    }

    public static float3 GetMatrixScale(float4x4 matrix)
    {
        float x = Length(matrix.c0);
        float y = Length(matrix.c1);
        float z = Length(matrix.c2);
        return new float3(x, y, z);
    }

    public static float3 MatrixMultiplyPoint3x4(float4x4 matrix, float4 pos)
    {
        return matrix.c0.xyz * pos.x + matrix.c1.xyz * pos.y + matrix.c2.xyz * pos.z + matrix.c3.xyz * pos.w;
    }

    // lossyScaleMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse * transform.local2World
    // or = (transform.worldToLocalMatrix * Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one)).inverse
    public static float GetLossyScaleX(TransformAccess transformAccess)
    {
        float4x4 world2Local = transformAccess.worldToLocalMatrix;
        float4x4 TR = float4x4.TRS(transformAccess.position, transformAccess.rotation, 1);
        float4x4 scaleMatrix = math.mul(world2Local, TR);
        return 1 / scaleMatrix.c0.x;
    }

    public static float Length(float2 vec)
    {
        double lengthSQ = math.lengthsq(vec);
        return lengthSQ > float.Epsilon ? (float)math.sqrt(lengthSQ) : 0f;
    }

    public static float Length(float3 vec)
    {
        double lengthSQ = math.lengthsq(vec);
        return lengthSQ > float.Epsilon ? (float)math.sqrt(lengthSQ) : 0f;
    }

    public static float Length(float4 vec)
    {
        double lengthSQ = math.lengthsq(vec);
        return lengthSQ > float.Epsilon ? (float)math.sqrt(lengthSQ) : 0f;
    }

    public static float2 Normalize(float2 vec)
    {
        double magsqr = math.dot(vec, vec);
        return magsqr > float.Epsilon ? (vec * new float2(math.rsqrt(magsqr))) : float2.zero;
    }

    public static float3 Normalize(float3 vec)
    {
        double magsqr = math.dot(vec, vec);
        return magsqr > float.Epsilon ? (vec * new float3(math.rsqrt(magsqr))) : float3.zero;
    }

    public static float4 Normalize(float4 vec)
    {
        double magsqr = math.dot(vec, vec);
        return magsqr > float.Epsilon ? (vec * new float4(math.rsqrt(magsqr))) : float4.zero;
    }

    //lossyscale有负缩放要补反方向... sign(parentScale)
    public static float3 TransformDirection(float3 parentWorldPos, quaternion parentWorldRot, float3 localDir)
    {
        float4x4 Local2WorldTR = float4x4.TRS(float3.zero, parentWorldRot, 1);
        //求个逆就有误差了..
        float4x4 Local2WorldTR_IT = math.transpose(math.inverse(Local2WorldTR));
        // return math.mul(Local2WorldTR_IT, new float4(localDir, 0)).xyz;
        return math.mul((float3x3)Local2WorldTR_IT, localDir).xyz;
    }

    public static float3 InverseTransformDirection(float3 parentWorldPos, quaternion parentWorldRot, float3 worldDir)
    {
        float4x4 World2LocalTR_I = float4x4.TRS(float3.zero, parentWorldRot, 1);
        float4x4 World2LocalTR_IT = math.transpose((World2LocalTR_I));
        return math.mul((float3x3)World2LocalTR_IT, worldDir).xyz;
    }

    // public static quaternion FromToRotation(float3 fromDirection, float3 toDirection)
    // {
    //     float fromSq = math.dot(fromDirection, fromDirection);
    //     float toSq = math.dot(toDirection, toDirection);
    //     if(fromSq <= float.Epsilon || toSq <= float.Epsilon)
    //         return quaternion.identity;
    //     
    //     float3 unitFrom = fromDirection * math.rsqrt(fromSq);
    //     float3 unitTo = toDirection * math.rsqrt(toSq);
    //     float d = math.dot(unitFrom, unitTo);
    //     if (d >= 1f)
    //     {
    //         return quaternion.identity;
    //     }
    //     else if(d <= -1f)
    //     {
    //         float3 axis = math.cross(unitFrom, right);
    //         return quaternion.AxisAngle(math.normalize(axis), PI);
    //     }
    //     else
    //     {
    //         float s = 1 + d;
    //         float3 v = math.cross(unitFrom, unitTo);
    //         quaternion result = new quaternion(v.x, v.y, v.z, s);
    //         return math.normalize(result);
    //     }
    // }

    public static quaternion FromToRotation(float3 from, float3 to)
    {
        float3
            v1 = Normalize(from),
            v2 = Normalize(to),
            cr = math.cross(v1, v2);
        float4 q = new float4(cr, 1 + math.dot(v1, v2));
        return math.normalize(q);
    }

    public static quaternion QuaternionMultiply(quaternion lhs, quaternion rhs)
    {
        return new quaternion(
            (float)((double)lhs.value.w * (double)rhs.value.x + (double)lhs.value.x * (double)rhs.value.w + (double)lhs.value.y * (double)rhs.value.z - (double)lhs.value.z * (double)rhs.value.y),
            (float)((double)lhs.value.w * (double)rhs.value.y + (double)lhs.value.y * (double)rhs.value.w + (double)lhs.value.z * (double)rhs.value.x - (double)lhs.value.x * (double)rhs.value.z),
            (float)((double)lhs.value.w * (double)rhs.value.z + (double)lhs.value.z * (double)rhs.value.w + (double)lhs.value.x * (double)rhs.value.y - (double)lhs.value.y * (double)rhs.value.x),
            (float)((double)lhs.value.w * (double)rhs.value.w - (double)lhs.value.x * (double)rhs.value.x - (double)lhs.value.y * (double)rhs.value.y - (double)lhs.value.z * (double)rhs.value.z));
    }

    // Rotates the point /point/ with /rotation/.
    public static float3 QuaternionMultiplyPoint(quaternion rotation, float3 point)
    {
        float x = rotation.value.x * 2F;
        float y = rotation.value.y * 2F;
        float z = rotation.value.z * 2F;
        float xx = rotation.value.x * x;
        float yy = rotation.value.y * y;
        float zz = rotation.value.z * z;
        float xy = rotation.value.x * y;
        float xz = rotation.value.x * z;
        float yz = rotation.value.y * z;
        float wx = rotation.value.w * x;
        float wy = rotation.value.w * y;
        float wz = rotation.value.w * z;

        float3 res;
        res.x = (1F - (yy + zz)) * point.x + (xy - wz) * point.y + (xz + wy) * point.z;
        res.y = (xy + wz) * point.x + (1F - (xx + zz)) * point.y + (yz - wx) * point.z;
        res.z = (xz - wy) * point.x + (yz + wx) * point.y + (1F - (xx + yy)) * point.z;
        return res;
    }

    public static quaternion QuaternionNormalize(quaternion q)
    {
        float mag = math.sqrt(math.dot(q, q));

        if (mag < math.EPSILON)
            return quaternion.identity;

        return new quaternion(q.value.x / mag, q.value.y / mag, q.value.z / mag, q.value.w / mag);
    }

    private static bool QuaternionIsEqualUsingDot(float dot)
    {
        // Returns false in the presence of NaN values.
        return dot > 1.0f - kEpsilon;
    }

    public static bool QuaternionEquals(quaternion lhs, quaternion rhs)
    {
        return QuaternionIsEqualUsingDot(math.dot(lhs, rhs));
    }

    public static float QuaternionAngle(quaternion a, quaternion b)
    {
        float dot = math.min(math.abs(math.dot(a, b)), 1.0F);
        return QuaternionIsEqualUsingDot(dot) ? 0.0f : math.acos(dot) * 2.0F * Rad2Deg;
    }

    public static quaternion QuaternionRotateTowards(quaternion from, quaternion to, float maxDegreesDelta)
    {
        float angle = QuaternionAngle(from, to);
        if (angle == 0.0f) return to;
        return QuaternionSlerpUnclamped(from, to, math.min(1.0f, maxDegreesDelta / angle));
    }

    public static quaternion QuaternionSlerpUnclamped(quaternion a, quaternion b, float t)
    {
        // 计算点积并处理对跖点（最短路径）
        float dot = math.dot(a, b);
        bool negative = dot < 0.0f;
        float absDot = math.abs(dot);
        float cosTheta = math.clamp(absDot, -1.0f, 1.0f);
        float theta = math.acos(cosTheta);

        if (math.abs(theta) < kEpsilon)
        {
            return b;
        }

        // 处理对跖点：反转其中一个四元数以确保最短路径
        quaternion target = negative ? QuaternionInverse(b) : b;
        float sinTheta = math.sin(theta);
        float invSinTheta = 1.0f / sinTheta;

        // 球面插值公式：a * sin((1-t)θ) + target * sin(tθ)
        float factorA = math.sin((1.0f - t) * theta) * invSinTheta;
        float factorB = math.sin(t * theta) * invSinTheta;

        quaternion result;
        result.value = a.value * factorA + target.value * factorB;
        result.value = math.normalize(result.value);

        return result;
    }
    
    public static quaternion QuaternionInverse(quaternion q)
    {
        float normSq = math.lengthsq(q);
        
        if (normSq < math.EPSILON)
        {
            return quaternion.identity;
        }

        float invNormSq = 1.0f * math.rcp(normSq);
        quaternion conjugate = new quaternion(q.value.x * -1, q.value.y * -1, q.value.z * -1, q.value.w);
        
        return new quaternion(
            conjugate.value.x * invNormSq,
            conjugate.value.y * invNormSq,
            conjugate.value.z * invNormSq,
            conjugate.value.w * invNormSq
        );
    }
}