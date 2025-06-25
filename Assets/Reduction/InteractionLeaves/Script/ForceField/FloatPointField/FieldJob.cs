using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct AddSourceJob : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<float> Back;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Source;

        [ReadOnly] private float deltaTime;

        public void Execute()
        {
            var srcPtr  = (float*)Source.GetUnsafeReadOnlyPtr();
            var destPtr = (float*)Back.GetUnsafePtr();

            for (int i = 0; i < Back.Length; i++)
                destPtr[i] += srcPtr[i] * deltaTime;
        }

        public void Execute(int startIndex, int count)
        {
            int end = count + startIndex;

            var srcPtr  = (float*)Source.GetUnsafeReadOnlyPtr();
            var destPtr = (float*)Back.GetUnsafePtr();

            for (int x = startIndex; x < end; x++)
                destPtr[x] += srcPtr[x] * deltaTime;
        }

        public void UpdateParams(ref NativeArray<float> back, ref NativeArray<float> source, float deltaTime)
        {
            Back           = back;
            Source         = source.AsReadOnly();
            this.deltaTime = deltaTime;
        }
    }
    
    //如果接受足够多的随机混合的"稳定" 是可以转高斯-塞德尔方式的
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct DiffuseLineJob : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<float> Back;

        [ReadOnly]
        private NativeArray<float>.ReadOnly Front;

        [ReadOnly] public int3 dim;
        [ReadOnly] public int3 N;
        [ReadOnly] public int4  stride;
        [ReadOnly] public int  iterateDirection;

        // [ReadOnly] private float diffusion; //[0,1]
        // [ReadOnly] private float deltaTime;

        [ReadOnly] private ConvolutionMethod convolutionMethod;

        [ReadOnly] private float2 divAndAbsorption;//预计算

        [ReadOnly] private int iterateCount;

        [ReadOnly] private int b;

        public void Execute()
        {
            (float div, float absorption) = (divAndAbsorption.x, divAndAbsorption.y);

            var srcPtr  = (float*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (float*)Back.GetUnsafePtr();
            
            //
            switch (convolutionMethod)
            {
                case ConvolutionMethod.LineSequence:
                    for (int k = 0; k < iterateCount; k++)
                    {
                        for (int y = 1; y <= N.y; y++)
                        {
                            for (int z = 1; z <= N.z; z++)
                            {
                                int index = GridUtils.GetIndex(1, y, z, dim);
                                
                                for (int i = 1; i <= N.x; i++)
                                {
                                    destPtr[index] = srcPtr[index] * div +
                                                     (destPtr[index - stride.x] +
                                                      destPtr[index + stride.x]) * absorption;
                                    
                                    index += stride.x;
                                }
                            }
                        }
                
                        for (int y = 1; y <= N.y; y++)
                        {
                            for (int x = 1; x <= N.x; x++)
                            {
                                int index = GridUtils.GetIndex(x, y, 1, dim);
                                for (int i = 1; i <= N.z; i++)
                                {
                                    destPtr[index] = srcPtr[index] * div +
                                                     (destPtr[index - stride.z] +
                                                      destPtr[index + stride.z]) * absorption;
                                    
                                    index += stride.z;
                                }
                            }
                        }
                
                        for (int z = 1; z <= N.y; z++)
                        {
                            for (int x = 1; x <= N.x; x++)
                            {
                                int index = GridUtils.GetIndex(x, 1, z, dim);
                                
                                for (int i = 1; i <= N.y; i++)
                                {
                                    destPtr[index] = srcPtr[index] * div +
                                                     (destPtr[index - stride.y] +
                                                      destPtr[index + stride.y]) * absorption;
                                    
                                    index += stride.y;
                                }
                            }
                        }
                        
                        //set_bnt

                        GridUtils.SetBoundary(ref destPtr, b, in stride, in N, in dim);
                    }
                    break;
                
                case ConvolutionMethod.LineSlidingWindow:
                    for (int k = 0; k < iterateCount; k++)
                    {
                        for (int y = 1; y <= N.y; y++)
                        {
                            for (int z = 1; z <= N.z; z++)
                            {
                                int index = GridUtils.GetIndex(1, y, z, dim);
                                
                                float left = destPtr[index - stride.x],
                                      mid  = destPtr[index], 
                                      right;

                                for (int i = 1; i <= N.x; i++)
                                {
                                    right = destPtr[index + stride.x];
                                    
                                    destPtr[index] = srcPtr[index] * div + (left + right) * absorption;

                                    (left, mid) = (mid, right);

                                    index += stride.x;
                                }
                            }
                        }
                
                        for (int y = 1; y <= N.y; y++)
                        {
                            for (int x = 1; x <= N.x; x++)
                            {
                                int index = GridUtils.GetIndex(x, y, 1, dim);
                                
                                float left = destPtr[index - stride.z],
                                      mid  = destPtr[index], 
                                      right;
                        
                                for (int i = 1; i <= N.z; i++)
                                {
                                    right = destPtr[index + stride.z];
                                    
                                    destPtr[index] = srcPtr[index] * div + (left + right) * absorption;
                        
                                    (left, mid) = (mid, right);
                        
                                    index += stride.z;
                                }
                            }
                        }

                        for (int z = 1; z <= N.y; z++)
                        {
                            for (int x = 1; x <= N.x; x++)
                            {
                                int index = GridUtils.GetIndex(x, 1, z, dim);

                                float left = destPtr[index - stride.y],
                                      mid  = destPtr[index],
                                      right;

                                for (int i = 1; i <= N.y; i++)
                                {
                                    right = destPtr[index + stride.y];

                                    destPtr[index] = srcPtr[index] * div + (left + right) * absorption;

                                    (left, mid) = (mid, right);

                                    index += stride.y;
                                }
                            }
                        }
                        //set_bnt
                        GridUtils.SetBoundary(ref destPtr, b, in stride, in N, in dim);
                    }
                    break;
            }
        }

        //输入范围[0，dim-2]即[0, N],需要在三个方向偏移一步
        public void Execute(int startIndex, int count)
        {
            // float a = deltaTime * diffusion * (N.x * N.y * N.z);
            //
            // float div        = 1 / (1 + 2 * a);
            // float absorption = a * div;
            
            (float div, float absorption) = (divAndAbsorption.x, divAndAbsorption.y);

            int3 headPos;
            switch (iterateDirection)
            {
                case 0: //x
                {
                    int yz = startIndex / count;
                    int z  = yz         % N.z;
                    int y  = yz         / N.z;

                    headPos = new int3(0, y, z);
                }
                    break;
                case 1: //y
                {
                    int xz = startIndex / count;

                    int x = xz % N.x,
                        z = xz / N.x;

                    headPos = new int3(x, 0 ,z);
                }
                    break;
                case 2: //z
                default:
                {
                    int xy = startIndex / count;
                    int x  = xy         % N.x;
                    int y  = xy         / N.x;
            
                    headPos = new int3(x, y, 0);
                }
                    break;
            }

            int  index = GridUtils.GetIndex(headPos + 1, dim);

            var srcPtr  = (float*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (float*)Back.GetUnsafePtr();

            switch (convolutionMethod)
            {
                case ConvolutionMethod.LineSequence:

                    for (int i = 0; i < count; i++)
                    {
                        
                        destPtr[index] = srcPtr[index] * div +
                                         (destPtr[index - stride[iterateDirection]] +
                                          destPtr[index + stride[iterateDirection]]) * absorption;
                    
                        index++;
                    }
                    break;
                
                case ConvolutionMethod.LineSlidingWindow:

                    float left = destPtr[index - stride[iterateDirection]],
                          mid  = destPtr[index], 
                          right;

                    for (int i = 0; i < count; i++)
                    {
                        right = destPtr[index + stride[iterateDirection]];

                        destPtr[index] = srcPtr[index] * div + (left + right) * absorption;

                        (left, mid) = (mid, right);

                        index++;
                    }
                    break;
            }
        }

        public void UpdateParams(ref NativeArray<float> back,             ref NativeArray<float> front,
                                 float2                 divAndAbsorption, ConvolutionMethod      convolution,
                                 int                    iterateCount = 1, int b = 0)
        {
            Back                   = back;
            Front                  = front.AsReadOnly();
            this.divAndAbsorption  = divAndAbsorption;
            this.convolutionMethod = convolution;
            this.iterateCount      = iterateCount;
            this.b                 = b;
        }
    }

    
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct DiffuseRBGSJob : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<float> Back;

        [ReadOnly]
        private NativeArray<float>.ReadOnly Front;

        [ReadOnly] public int3 dim;
        [ReadOnly] public int3 N;
        [ReadOnly] public int4 stride;
        
        [ReadOnly] private float2 divAndAbsorption;

        [ReadOnly] private int iterateCount;

        [ReadOnly] private int  b;
        
        [ReadOnly] public  bool IsRedPhase; // 当前是红点还是黑点阶段

        public void Execute()
        {
            (float div, float absorption) = (divAndAbsorption.x, divAndAbsorption.y);

            var srcPtr  = (float*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (float*)Back.GetUnsafePtr();

            for (int k = 0; k < iterateCount; k++)
            {
                bool readAlpha = false;
                do
                {
                    for (int y = 1; y <= N.y; y++)
                    {
                        for (int z = 1; z <= N.z; z++)
                        {
                            bool isRed = ((y + z) & 1) == 0;
                        
                            int  index = GridUtils.GetIndex(1, y, z, dim);

                            int end = index + N.x;

                            index = math.select(index, index + 1, isRed != readAlpha);

                            for (; index < end; index += 2)
                            {
                                float newValue = srcPtr[index] * div +
                                                 (destPtr[index - stride.x] + destPtr[index + stride.x] +
                                                  destPtr[index - stride.y] + destPtr[index + stride.y] +
                                                  destPtr[index - stride.z] + destPtr[index + stride.z]) * absorption;

                                destPtr[index] = newValue;
                                // destPtr[index] = math.lerp(destPtr[index], newValue, 1.5f);//1.0-1.8
                            }

                        }
                    }

                    readAlpha = !readAlpha;
                } while (readAlpha);

                GridUtils.SetBoundary(ref destPtr, b, in stride, in N, in dim);
            }

        }

        //x方向，输入batchCount = N.x
        public void Execute(int startIndex, int count)
        {
            //   
            (float div, float absorption) = (divAndAbsorption.x, divAndAbsorption.y);

            int yz = startIndex / count;
            int z  = yz         % N.z;
            int y  = yz         / N.z;

            bool isRed = ((y + z) & 1) == 0;
            
            int3 pos   = new int3(0, y, z);

            int  index = GridUtils.GetIndex(pos + 1, dim);

            int end = index + count;

            index = math.select(index, index + 1, isRed == IsRedPhase);

            var srcPtr  = (float*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (float*)Back.GetUnsafePtr();

            for (; index < end; index += 2)
            {
                float newValue = srcPtr[index] * div +
                                 (destPtr[index - stride.x] + destPtr[index + stride.x] +
                                  destPtr[index - stride.y] + destPtr[index + stride.y] +
                                  destPtr[index - stride.z] + destPtr[index + stride.z]) * absorption;

                destPtr[index] = newValue;
                // destPtr[index] = math.lerp(destPtr[index], newValue, 1.5f);//1.0-1.8
            }
        }

        public void UpdateParams(ref NativeArray<float> back, ref NativeArray<float> front,
                                 float2                 divAndAbsorption,
                                 int                    iterateCount = 1, int b = 0)
        {
            Back                  = back;
            Front                 = front.AsReadOnly();
            this.divAndAbsorption = divAndAbsorption;
            this.iterateCount     = iterateCount;
            this.b                = b;
        }
    }

    /// <summary>
    /// length=6, batch=1
    /// 舒适,but 只分6个任务，实际时间又不如按行并行
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct SetBoundaryJob : IJobParallelFor, IJob
    {
        [NativeDisableParallelForRestriction] private NativeArray<float> Data;

        [ReadOnly] public  int3 dim;
        [ReadOnly] public  int3 N;
        [ReadOnly] public  int4 stride;
        [ReadOnly] private int  b; // 边界类型 0密度  1x  2y  3z 速度

        public void Execute()
        {
            var ptr = (float*)Data.GetUnsafePtr();
            GridUtils.SetBoundary(ref ptr, b, in stride, in N, in dim);
        }

        public void Execute(int index)
        {
            int xStart = 0, xEnd = dim.x - 1;
            int yStart = 0, yEnd = dim.y - 1;
            int zStart = 0, zEnd = dim.z - 1;
            switch (index)
            {
                case 0: //left 整面 - 2边
                    /*xStart = 0*;*/xEnd = 0;
                    zStart   = 1; zEnd = dim.z - 2;
                    break;
                case 1: //right 整面 - 2边
                    xStart = dim.x - 1; /*xEnd = dim.x - 1;*/
                    zStart = 1; zEnd = dim.z - 2;
                    break;
                case 2: //buttom 内面
                    /*yStart = 0;*/ yEnd = 0; 
                    xStart = 1; xEnd = dim.x - 2; 
                    zStart = 1; zEnd = dim.z - 2;
                    break;
                case 3: //up 内面
                    yStart = dim.y - 1; /*yEnd = dim.y - 1;*/
                    xStart = 1; xEnd = dim.x - 2;
                    zStart = 1; zEnd = dim.z - 2;
                    break;
                case 4: //back 整面
                    /*zStart = 0;*/ zEnd = 0;
                    break;
                case 5: //forward 整面
                    zStart = dim.z - 1; /*zEnd = dim.z - 1;*/
                    break;
            }
            
            for (int z = zStart; z <= zEnd; z++)
            {
                for (int y = yStart; y <= yEnd; y++)
                {
                    for (int x = xStart; x <= xEnd; x++)
                    {
                        int3 pos   = new int3(x, y, z);
                        //到6个面的最小距离
                        int3 boxMinDis = math.min(pos, dim - 1 - pos);

                        // 处理边界点
                        Data[GridUtils.GetIndex(pos, dim)] = (b == 0)
                            ? HandleDensityBoundary(pos, boxMinDis)
                            : HandleVelocityBoundary(pos, boxMinDis);
                    }
                }
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float HandleDensityBoundary(int3 pos, int3 boxMinDis)
        {
            // 密度场: 取最近内部点值
            int3 innerPos                    = pos;
            if (boxMinDis.x == 0) innerPos.x = pos.x == 0 ? 1 : dim.x - 2;
            if (boxMinDis.y == 0) innerPos.y = pos.y == 0 ? 1 : dim.y - 2;
            if (boxMinDis.z == 0) innerPos.z = pos.z == 0 ? 1 : dim.z - 2;

            return Data[GridUtils.GetIndex(innerPos, dim)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float HandleVelocityBoundary(int3 pos, int3 boxMinDis)
        {
            float value      = 0;
            int   components = 0;

//            switch (b)
//            {
//                case 0:
//                    break;
//                case 1:
//                    break;
//                case 2:
//                    break;
//                case 3:
//                    break;
//            }
            // 处理每个边界方向,分支过多
            if (boxMinDis.x == 0)
            {
                int3 neighbor = pos;
                neighbor.x =  pos.x == 0 ? 1 : dim.x - 2;
                value      += (b == 1) ? -Data[GridUtils.GetIndex(neighbor, dim)] : Data[GridUtils.GetIndex(neighbor, dim)];
                components++;
            }

            if (boxMinDis.y == 0)
            {
                int3 neighbor = pos;
                neighbor.y =  pos.y == 0 ? 1 : dim.y - 2;
                value      += (b == 2) ? -Data[GridUtils.GetIndex(neighbor, dim)] : Data[GridUtils.GetIndex(neighbor, dim)];
                components++;
            }

            if (boxMinDis.z == 0)
            {
                int3 neighbor = pos;
                neighbor.z =  pos.z == 0 ? 1 : dim.z - 2;
                value      += (b == 3) ? -Data[GridUtils.GetIndex(neighbor, dim)] : Data[GridUtils.GetIndex(neighbor, dim)];
                components++;
            }

//                return value / components;

            return components switch
                   {
                       1 => value,
                       2 => value * 0.5f,
                       3 => value * 0.33333333f,
                       _ => 0
                   };
        }

        public void UpdateParams(ref NativeArray<float> data, int b)
        {
            Data   = data;
            this.b = b;
        }
    }

    //move
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  // Debug = true,
                  DisableSafetyChecks = true)]
    internal unsafe struct ReverseAdvectDensityJob : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<float> Back;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Front;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly X;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Y;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Z;

        [ReadOnly] public int3   dim;
        [ReadOnly] public int3   N;
        [ReadOnly] public int4   stride;
        [ReadOnly] public float3 maxRange;
        
        [ReadOnly] private const float min = float.MinValue / 2;
        [ReadOnly] private const float max = float.MaxValue / 2;

        [ReadOnly] private float deltaTime;
        [ReadOnly] private float ratio;

        public void Execute()
        {
            var srcPtr  = (float*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (float*)Back.GetUnsafePtr();
            var xPtr    = (float*)X.GetUnsafeReadOnlyPtr();
            var yPtr    = (float*)Y.GetUnsafeReadOnlyPtr();
            var zPtr    = (float*)Z.GetUnsafeReadOnlyPtr();

            float3 dt0 = deltaTime * (float3)N;

            for (int y = 1; y <= N.y; y++)
            {
                for (int z = 1; z <= N.z; z++)
                {
                    int3 pos = new int3(1, y, z);

                    int index = GridUtils.GetIndex(pos, dim);
                    int end   = index + N.x;
                    
                    for (; index < end; index++)
                    {
                        float3 vel = new float3(xPtr[index], yPtr[index], zPtr[index]);

                        float3 prePos = pos - dt0 * vel;

                        prePos = math.clamp(prePos, 0.5f, maxRange);

                        float result = GridUtils.TrilinearStandard(prePos, ref srcPtr, dim) * ratio;

                        GridUtils.AtomicAddSat(ref destPtr, index, result, min, max);

                        pos.x++;
                    }
                }
            }

        }

        public void Execute(int startIndex, int count)
        {
            var srcPtr  = (float*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (float*)Back.GetUnsafePtr();
            var xPtr    = (float*)X.GetUnsafeReadOnlyPtr();
            var yPtr    = (float*)Y.GetUnsafeReadOnlyPtr();
            var zPtr    = (float*)Z.GetUnsafeReadOnlyPtr();
            
            int3 pos = GridUtils.GetGridPos(startIndex, N) + 1;

            float3 dt0 = deltaTime * (float3)N;

            int index = GridUtils.GetIndex(pos, dim);
            int end   = index + count;

            int3 dir = new int3(1, 0, 0);
            
            for (; index < end; index++)
            {
                float3 vel = new float3(xPtr[index], yPtr[index], zPtr[index]);

                float3 prePos = pos - dt0 * vel;

                prePos = math.clamp(prePos, 0.5f, maxRange);

                float result = GridUtils.TrilinearStandard(prePos, ref srcPtr, dim) * ratio;

                GridUtils.AtomicAddSat(ref destPtr, index, result, min, max);

                pos.x++;
            }
        }

        public void UpdateParams(ref NativeArray<float> back,      
                                 ref NativeArray<float> front, 
                                 ref NativeArray<float> x, 
                                 ref NativeArray<float> y, 
                                 ref NativeArray<float> z,
                                 float                  deltaTime,
                                 float                  ratio)
        {
            Back           = back;
            Front          = front.AsReadOnly();
            X              = x.AsReadOnly();
            Y              = y.AsReadOnly();
            Z              = z.AsReadOnly();
            this.deltaTime = deltaTime;
            this.ratio     = ratio;
        }
    }


    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default,
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  // Debug = true,
                  DisableSafetyChecks = true)]
    internal unsafe struct ForwardAdvectDensityJob : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<float> Back;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Front;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly X;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Y;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Z;

        [ReadOnly] public int3   dim;
        [ReadOnly] public int3   N;
        [ReadOnly] public int4   stride;
        [ReadOnly] public float3 maxRange;
        
        [ReadOnly] private const float min = float.MinValue / 32;
        [ReadOnly] private const float max = float.MaxValue / 32;

        [ReadOnly] private float deltaTime;
        [ReadOnly] private float ratio;

        public void Execute()
        {
            var srcPtr  = (float*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (float*)Back.GetUnsafePtr();
            var xPtr    = (float*)X.GetUnsafeReadOnlyPtr();
            var yPtr    = (float*)Y.GetUnsafeReadOnlyPtr();
            var zPtr    = (float*)Z.GetUnsafeReadOnlyPtr();

            float3 dt0 = deltaTime * (float3)N;

            for (int y = 1; y <= N.y; y++)
            {
                for (int z = 1; z <= N.z; z++)
                {
                    int3 pos = new int3(1, y, z);

                    int index = GridUtils.GetIndex(pos, dim);
                    int end   = index + N.x;
                    
                    for (; index < end; index++)
                    {
                        float3 vel = new float3(xPtr[index], yPtr[index], zPtr[index]);

                        float energy = srcPtr[index] * ratio;

                        float3 prePos = pos + dt0 * vel;
                        
                        prePos = math.clamp(prePos, 0.5f, maxRange);

                        GridUtils.TrilinearStandardAtomicWrite(prePos, ref destPtr, dim,
                                                               energy, min, max);
                        pos.x++;
                    }
                }
            }

        }

        public void Execute(int startIndex, int count)
        {
            var srcPtr  = (float*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (float*)Back.GetUnsafePtr();
            var xPtr    = (float*)X.GetUnsafeReadOnlyPtr();
            var yPtr    = (float*)Y.GetUnsafeReadOnlyPtr();
            var zPtr    = (float*)Z.GetUnsafeReadOnlyPtr();
            
            int3 pos = GridUtils.GetGridPos(startIndex, N) + 1;

            float3 dt0 = deltaTime * (float3)N;

            int index = GridUtils.GetIndex(pos, dim);
            int end   = index + count;

            int3 dir = new int3(1, 0, 0);
            
            for (; index < end; index++)
            {
                float3 vel = new float3(xPtr[index], yPtr[index], zPtr[index]);

                float energy = srcPtr[index] * ratio;

                float3 prePos = pos - dt0 * vel;

                prePos = math.clamp(prePos, 0.5f, maxRange);

                GridUtils.TrilinearStandardAtomicWrite(prePos, ref destPtr, dim,
                                                       energy, min, max);
                
                pos.x++;
            }
        }

        public void UpdateParams(ref NativeArray<float> back,      
                                 ref NativeArray<float> front, 
                                 ref NativeArray<float> x, 
                                 ref NativeArray<float> y, 
                                 ref NativeArray<float> z,
                                 float                  deltaTime,
                                 float                  ratio)
        {
            Back           = back;
            Front          = front.AsReadOnly();
            X              = x.AsReadOnly();
            Y              = y.AsReadOnly();
            Z              = z.AsReadOnly();
            this.deltaTime = deltaTime;
            this.ratio     = ratio;
        }
    }
    

    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default,
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  // Debug = true,
                  DisableSafetyChecks = true)]
    internal unsafe struct ReverseAdvectVelocityJob : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<float> BackX;

        [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<float> BackY;
        
        [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<float> BackZ;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly X;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Y;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Z;

        [ReadOnly] public int3   dim;
        [ReadOnly] public int3   N;
        [ReadOnly] public int4   stride;
        [ReadOnly] public float3 maxRange;
        
        [ReadOnly] private const float min = float.MinValue / 2;
        [ReadOnly] private const float max = float.MaxValue / 2;
        
        [ReadOnly] private float deltaTime;
        [ReadOnly] private float ratio;

        public void Execute()
        {
            var dxPtr = (float*)BackX.GetUnsafePtr();
            var dyPtr = (float*)BackY.GetUnsafePtr();
            var dzPtr = (float*)BackZ.GetUnsafePtr();
            var xPtr  = (float*)X.GetUnsafeReadOnlyPtr();
            var yPtr  = (float*)Y.GetUnsafeReadOnlyPtr();
            var zPtr  = (float*)Z.GetUnsafeReadOnlyPtr();

            float3 dt0 = deltaTime * (float3)N;

            for (int y = 1; y <= N.y; y++)
            {
                for (int z = 1; z <= N.z; z++)
                {
                    int3 pos = new int3(1, y, z);

                    int index = GridUtils.GetIndex(pos, dim);
                    int end   = index + N.x;

                    for (; index < end; index++)
                    {
                        float3 vel = new float3(xPtr[index], yPtr[index], zPtr[index]);

                        float3 prePos = pos - dt0 * vel;

                        prePos = math.clamp(prePos, 0.5f, maxRange);

                        float3 result = GridUtils.TrilinearStandard(prePos, ref xPtr, ref yPtr, ref zPtr, dim) * ratio;

                        GridUtils.AtomicAddSat(ref dxPtr, index, result.x, min, max);
                        GridUtils.AtomicAddSat(ref dyPtr, index, result.y, min, max);
                        GridUtils.AtomicAddSat(ref dzPtr, index, result.z, min, max);

                        pos.x++;
                    }
                }
            }

        }

        public void Execute(int startIndex, int count)
        {
            var dxPtr = (float*)BackX.GetUnsafePtr();
            var dyPtr = (float*)BackY.GetUnsafePtr();
            var dzPtr = (float*)BackZ.GetUnsafePtr();
            var xPtr  = (float*)X.GetUnsafeReadOnlyPtr();
            var yPtr  = (float*)Y.GetUnsafeReadOnlyPtr();
            var zPtr  = (float*)Z.GetUnsafeReadOnlyPtr();
            
            int3 pos = GridUtils.GetGridPos(startIndex, N) + 1;

            float3 dt0 = deltaTime * (float3)N;

            int index = GridUtils.GetIndex(pos, dim);
            int end   = index + count;
            
            for (; index < end; index++)
            {
                float3 vel = new float3(xPtr[index], yPtr[index], zPtr[index]);

                float3 prePos = pos - dt0 * vel;

                prePos = math.clamp(prePos, 0.5f, maxRange);

                float3 result = GridUtils.TrilinearStandard(prePos, ref xPtr, ref yPtr, ref zPtr, dim) * ratio;

                GridUtils.AtomicAddSat(ref dxPtr, index, result.x, min, max);
                GridUtils.AtomicAddSat(ref dyPtr, index, result.y, min, max);
                GridUtils.AtomicAddSat(ref dzPtr, index, result.z, min, max);
                
                pos.x++;
            }
        }

        public void UpdateParams(ref NativeArray<float> backx,      
                                 ref NativeArray<float> backy, 
                                 ref NativeArray<float> backz, 
                                 ref NativeArray<float> x, 
                                 ref NativeArray<float> y, 
                                 ref NativeArray<float> z,
                                 float                  deltaTime,
                                 float                  ratio)
        {
            BackX          = backx;
            BackY          = backy;
            BackZ          = backz;
            X              = x.AsReadOnly();
            Y              = y.AsReadOnly();
            Z              = z.AsReadOnly();
            this.deltaTime = deltaTime;
            this.ratio     = ratio;
        }
    }

    
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default,
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  // Debug = true,
                  DisableSafetyChecks = true)]
    internal unsafe struct ForwardAdvectVelocityJob : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<float> BackX;

        [NativeDisableParallelForRestriction]
        private NativeArray<float> BackY;
        
        [NativeDisableParallelForRestriction]
        private NativeArray<float> BackZ;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly X;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Y;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Z;

        [ReadOnly] public int3   dim;
        [ReadOnly] public int3   N;
        [ReadOnly] public int4   stride;
        [ReadOnly] public float3 maxRange;
        
        [ReadOnly] private const float min = float.MinValue / 2;
        [ReadOnly] private const float max = float.MaxValue / 2;

        [ReadOnly] private float deltaTime;
        [ReadOnly] private float ratio;

        public void Execute()
        {
            var dxPtr = (float*)BackX.GetUnsafePtr();
            var dyPtr = (float*)BackY.GetUnsafePtr();
            var dzPtr = (float*)BackZ.GetUnsafePtr();
            var xPtr  = (float*)X.GetUnsafeReadOnlyPtr();
            var yPtr  = (float*)Y.GetUnsafeReadOnlyPtr();
            var zPtr  = (float*)Z.GetUnsafeReadOnlyPtr();

            float3 dt0 = deltaTime * (float3)N;

            for (int y = 1; y <= N.y; y++)
            {
                for (int z = 1; z <= N.z; z++)
                {
                    int3 pos = new int3(1, y, z);

                    int index = GridUtils.GetIndex(pos, dim);
                    int end   = index + N.x;

                    for (; index < end; index++)
                    {
                        float3 vel = new float3(xPtr[index], yPtr[index], zPtr[index]);

                        float3 prePos = pos + dt0 * vel;

                        prePos = math.clamp(prePos, 0.5f, maxRange);
                        
                        GridUtils.TrilinearStandardAtomicWrite(prePos, ref dxPtr, ref dyPtr, ref dzPtr,
                                                               dim, vel * ratio, min, max);

                        pos.x++;
                    }
                }
            }
            
        }

        public void Execute(int startIndex, int count)
        {
            var dxPtr = (float*)BackX.GetUnsafePtr();
            var dyPtr = (float*)BackY.GetUnsafePtr();
            var dzPtr = (float*)BackZ.GetUnsafePtr();
            var xPtr  = (float*)X.GetUnsafeReadOnlyPtr();
            var yPtr  = (float*)Y.GetUnsafeReadOnlyPtr();
            var zPtr  = (float*)Z.GetUnsafeReadOnlyPtr();
            
            int3 pos = GridUtils.GetGridPos(startIndex, N) + 1;

            float3 dt0 = deltaTime * (float3)N;

            int index = GridUtils.GetIndex(pos, dim);
            int end   = index + count;
            
            for (; index < end; index++)
            {
                float3 vel = new float3(xPtr[index], yPtr[index], zPtr[index]);

                float3 prePos = pos + dt0 * vel;

                prePos = math.clamp(prePos, 0.5f, maxRange);
                        
                GridUtils.TrilinearStandardAtomicWrite(prePos, ref dxPtr, ref dyPtr, ref dzPtr,
                                                       dim, vel * ratio, min, max);
                
                pos.x++;
            }
        }

        public void UpdateParams(ref NativeArray<float> backx,      
                                 ref NativeArray<float> backy, 
                                 ref NativeArray<float> backz,
                                 ref NativeArray<float> x,
                                 ref NativeArray<float> y,
                                 ref NativeArray<float> z,
                                 float                  deltaTime,
                                 float                  ratio)
        {
            BackX          = backx;
            BackY          = backy;
            BackZ          = backz;
            X              = x.AsReadOnly();
            Y              = y.AsReadOnly();
            Z              = z.AsReadOnly();
            this.deltaTime = deltaTime;
            this.ratio     = ratio;
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct DivergenceJob : IJobParallelForBatch, IJob
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly X;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Y;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Z;

        [WriteOnly, NativeDisableParallelForRestriction]
        private NativeArray<float> Pressure;

        [WriteOnly, NativeDisableParallelForRestriction]
        private NativeArray<float> Divergence;

        [ReadOnly] public int3 dim;
        
        [ReadOnly] public int3 N;
        
        [ReadOnly] public int4 stride;

        public void Execute()
        {
            float3 h = - 0.5f * MathematicsUtil.one / N;
            
            var xPtr = (float*)X.GetUnsafeReadOnlyPtr();
            var yPtr = (float*)Y.GetUnsafeReadOnlyPtr();
            var zPtr = (float*)Z.GetUnsafeReadOnlyPtr();

            var divPtr = (float*)Divergence.GetUnsafePtr();
            var prePtr = (float*)Pressure.GetUnsafePtr();

            for (int y = 1; y <= N.y; y++)
            {
                for (int z = 1; z <= N.z; z++)
                {
                    int  index = GridUtils.GetIndex(1, y, z, dim);
                    for (int i = 0; i <= N.x; i++)
                    {
                        divPtr[index] = h.x * (xPtr[index + stride.x] - xPtr[index - stride.x]) +
                                        h.y * (yPtr[index + stride.y] - yPtr[index - stride.y]) +
                                        h.z * (zPtr[index + stride.z] - zPtr[index - stride.z]);
                
                        prePtr[index] = 0;

                        index += stride.x;
                    }
                }
            }

            GridUtils.SetBoundary(ref divPtr, 0, in stride, in N, in dim);
            GridUtils.SetBoundary(ref prePtr, 0, in stride, in N, in dim);
        }

        public void Execute(int startIndex, int count)
        {
            float3 h = - 0.5f * MathematicsUtil.one / N;

            int yz = startIndex / count;
            int z  = yz         % N.z;
            int y  = yz         / N.z;

            int3 pos   = new int3(0, y, z);
            int  index = GridUtils.GetIndex(pos + 1, dim);
            
            var  xPtr   = (float*)X.GetUnsafeReadOnlyPtr();
            var  yPtr   = (float*)Y.GetUnsafeReadOnlyPtr();
            var  zPtr   = (float*)Z.GetUnsafeReadOnlyPtr();

            var divPtr = (float*)Divergence.GetUnsafePtr();
            var prePtr = (float*)Pressure.GetUnsafePtr();
            
            for (int i = 0; i <  count; i++)
            {
                
                divPtr[index] = h.x * (xPtr[index + stride.x] - xPtr[index - stride.x]) +
                                h.y * (yPtr[index + stride.y] - yPtr[index - stride.y]) +
                                h.z * (zPtr[index + stride.z] - zPtr[index - stride.z]);
                
                prePtr[index] = 0;

                index += stride.x;
            }
        }

        public void UpdateParams(ref NativeArray<float> pressure, 
                                 ref NativeArray<float> divergence, 
                                 ref NativeArray<float> x, 
                                 ref NativeArray<float> y, 
                                 ref NativeArray<float> z)
        {
            X          = x.AsReadOnly();
            Y          = y.AsReadOnly();
            Z          = z.AsReadOnly();
            Pressure   = pressure;
            Divergence = divergence;
        }
        
        
    }
    //set bnt 0 div p
    

    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct PressureJob : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<float> Pressure;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Divergence;

        [ReadOnly] public int3 dim;
        
        [ReadOnly] public int3 N;
        
        [ReadOnly] public int4 stride;

        [ReadOnly] private int iterateCount;
        
        [ReadOnly] public bool IsRedPhase; 
        
        [ReadOnly] private const float inv = 0.16666667f;

        public void Execute()
        {
            var divPtr = (float*)Divergence.GetUnsafeReadOnlyPtr();
            var prePtr = (float*)Pressure.GetUnsafePtr();

            for (int k = 0; k < iterateCount; k++)
            {
                bool readAlpha = false;
                do
                {
                    for (int y = 1; y <= N.y; y++)
                    {
                        for (int z = 1; z <= N.z; z++)
                        {
                            bool isRed = ((y + z) & 1) == 0;
                        
                            int3 pos   = new int3(1, y, z);
                            int  index = GridUtils.GetIndex(pos, dim);
                        
                            int end = index + N.x;
                        
                            index = math.select(index, index + 1, isRed != readAlpha);//

                            for (; index < end; index += 2)
                            {
                                prePtr[index] = (divPtr[index]            +
                                                 prePtr[index - stride.x] + prePtr[index + stride.x] +
                                                 prePtr[index - stride.y] + prePtr[index + stride.y] +
                                                 prePtr[index - stride.z] + prePtr[index + stride.z]) * inv;
                            }
                        }
                    }
                    
                    readAlpha = !readAlpha;
                } while (readAlpha);

                GridUtils.SetBoundary(ref prePtr, 0, in stride, in N, in dim);
            }
        }

        public void Execute(int startIndex, int count)
        {
            int yz = startIndex / count;
            int z  = yz         % N.z;
            int y  = yz         / N.z;

            bool isRed = ((y + z) & 1) == 0;

            int3 pos   = new int3(0, y, z);
            int  index = GridUtils.GetIndex(pos + 1, dim);

            int end = index + count;

            index = math.select(index, index + 1, isRed == IsRedPhase);

            var divPtr = (float*)Divergence.GetUnsafeReadOnlyPtr();
            var prePtr = (float*)Pressure.GetUnsafePtr();

            for (; index < end; index += 2)
            {
                prePtr[index] = (divPtr[index]            +
                                 prePtr[index - stride.x] + prePtr[index + stride.x] +
                                 prePtr[index - stride.y] + prePtr[index + stride.y] +
                                 prePtr[index - stride.z] + prePtr[index + stride.z]) * inv;
                
            }
        }
        
        public void UpdateParams(ref NativeArray<float> pressure, ref NativeArray<float> divergence, int iterate)
        {
            Pressure     = pressure;
            Divergence   = divergence.AsReadOnly();
            iterateCount = iterate;
        }
    }
    //set bnt p
    

    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct CorrectVelocityJob : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<float> X;

        [NativeDisableParallelForRestriction]
        private NativeArray<float> Y;

        [NativeDisableParallelForRestriction]
        private NativeArray<float> Z;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Pressure;

        [ReadOnly] public int3 dim;
        
        [ReadOnly] public int3 N;
        
        [ReadOnly] public int4 stride;

        public void Execute()
        {
            float3 H = - 0.5f * (float3)N;
            
            var xPtr   = (float*)X.GetUnsafePtr();
            var yPtr   = (float*)Y.GetUnsafePtr();
            var zPtr   = (float*)Z.GetUnsafePtr();
            var prePtr = (float*)Pressure.GetUnsafeReadOnlyPtr();

            for (int y = 1; y <= N.y; y++)
            {
                for (int z = 1; z <= N.z; z++)
                {
                    int3 pos   = new int3(1, y, z);
                    
                    int  index = GridUtils.GetIndex(pos, dim);

                    for (int i = 0; i < N.x; i++)
                    {
                        xPtr[index] += H.x * (prePtr[index + stride.x] - prePtr[index - stride.x]);
                        yPtr[index] += H.y * (prePtr[index + stride.y] - prePtr[index - stride.y]);
                        zPtr[index] += H.z * (prePtr[index + stride.z] - prePtr[index - stride.z]);
                
                        // index += stride.x;
                        index ++;
                    }
                }
            }
            
            GridUtils.SetBoundary(ref xPtr, 1, in stride, in N, in dim);
            GridUtils.SetBoundary(ref yPtr, 2, in stride, in N, in dim);
            GridUtils.SetBoundary(ref zPtr, 3, in stride, in N, in dim);
        }

        public void Execute(int startIndex, int count)
        {
            float3 H = - 0.5f * (float3)N;

            int yz = startIndex / count;
            int z  = yz         % N.z;
            int y  = yz         / N.z;

            int3 pos   = new int3(0, y, z);
            int  index = GridUtils.GetIndex(pos + 1, dim);
            
            var xPtr   = (float*)X.GetUnsafePtr();
            var yPtr   = (float*)Y.GetUnsafePtr();
            var zPtr   = (float*)Z.GetUnsafePtr();
            var prePtr = (float*)Pressure.GetUnsafeReadOnlyPtr();

            for (int i = 0; i < count; i++)
            {
                xPtr[index] += H.x * (prePtr[index + stride.x] - prePtr[index - stride.x]);
                yPtr[index] += H.y * (prePtr[index + stride.y] - prePtr[index - stride.y]);
                zPtr[index] += H.z * (prePtr[index + stride.z] - prePtr[index - stride.z]);
                
                // index += stride.x;
                index ++;
            }
        }
        
        
        public void UpdateParams(ref NativeArray<float> pressure, 
                                 ref NativeArray<float> x, ref NativeArray<float> y, ref NativeArray<float> z)
        {
            X        = x;
            Y        = y;
            Z        = z;
            Pressure = pressure.AsReadOnly();
        }
    }
    // set bnt x y z


    //两边共用
    [BurstCompile]
    internal unsafe struct WindFieldClearDataJob<T> : IJob where T : unmanaged
    {
        [WriteOnly, NativeDisableParallelForRestriction]
        private NativeArray<T> Data;
        
        public void Execute()
        {
            var ptr  = Data.GetUnsafePtr();
            UnsafeUtility.MemSet(ptr, 0x00, UnsafeUtility.SizeOf<T>() * Data.Length);
        }
        
        public void UpdateParams(ref NativeArray<T> data)
        {
            Data         = data;
        }
    }


    //模拟结束 结果保存 下一帧其他系统取用
    [BurstCompile]
    internal unsafe struct WindSimulateSaveJob : IJob
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Back;

        [WriteOnly, NativeDisableParallelForRestriction]
        private NativeArray<float> ExchangeData;

        [ReadOnly] public int size;

        public void Execute()
        {
            var destPtr = ExchangeData.GetUnsafePtr();
            var srcPtr  = Back.GetUnsafeReadOnlyPtr();
            UnsafeUtility.MemCpy(destPtr, srcPtr, size);
        }

        public void UpdateParams(ref NativeArray<float> back, ref NativeArray<float> data)
        {
            Back         = back.AsReadOnly();
            ExchangeData = data;
        }
    }
    
    
    //外部系统写入exchange buffer
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  // Debug = true,
                  DisableSafetyChecks = true)]
    internal unsafe struct WriteDensityFieldJob : IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<float> D;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDCustomColliderInfo>.ReadOnly Rigibodys;
        
        [ReadOnly] public int3   dim;
        // [ReadOnly] public int3   N;
        // [ReadOnly] public float3 maxRange;

        [ReadOnly] private PBDBounds windFieldBounds;
        [ReadOnly] private float3    ori;
        [ReadOnly] public bool      super;

        public void Execute()
        {
            var denPtr = (float*)D.GetUnsafePtr();
            var rigPtr = (PBDCustomColliderInfo*)Rigibodys.GetUnsafeReadOnlyPtr();

            int3   dim      = math.select(this.dim, this.dim * 2, super);
            float3 maxRange = new float3(dim) - 1f;
            float3 ori      = math.select(this.ori, this.ori - 0.25f, super);
            float  scaleInv = math.select(1, 2, super);
            float  scale    = 1 / scaleInv;

            for (int i = 0; i < Rigibodys.Length; i++)
            {
                var rigi   = rigPtr[i];
                var bounds = rigi.Bounds;
                if(!MathematicsUtil.AABBOverlap(bounds, windFieldBounds))
                    continue;
                
                int3 Min = new (math.max(math.floor((bounds.Min - ori) * scaleInv), 0)),
                     Max = new (math.min(math.ceil((bounds.Max  - ori) * scaleInv), maxRange));

                int3 Size = Max - Min;

                //刚体还得考虑超薄。。
                // if (rigi.IsLargeVolume)
                {
                    for (int y = Min.y; y <= Max.y; y++)
                    {
                        for (int z = Min.z; z <= Max.z; z++)
                        {
                            int3   pos   = new int3(Min.x, y, z);
                            
                            float3 wpos  = new float3(pos) * scale + ori;
                            
                            int index = GridUtils.GetIndex(pos, dim);
                            int end   = index + Size.x;
                            
                            for (; index <= end; index++)
                            {
                                var result = rigi.AddDensityValue(wpos);

                                denPtr[index] += result;

                                wpos.x += scale;
                            }
                        }
                    }
                }
            }
        }

        public void UpdateParams(ref NativeArray<float>                d,
                                 in  float3                            ori,
                                 in  PBDBounds                         fieldBounds,
                                 ref NativeList<PBDCustomColliderInfo> rigibodys)
        {
            D               = d;
            this.ori        = ori;
            windFieldBounds = fieldBounds;
            Rigibodys       = rigibodys.AsParallelReader();
        }
    }
    
    
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true
                  )]
    internal unsafe struct WriteVelocityFieldJob : IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<float> X;

        [NativeDisableParallelForRestriction]
        private NativeArray<float> Y;

        [NativeDisableParallelForRestriction]
        private NativeArray<float> Z;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<PBDForceField>.ReadOnly ForceFields;

        [ReadOnly] public int3   dim;
        // [ReadOnly] public int3   N;
        // [ReadOnly] public float3 maxRange;

        [ReadOnly] private PBDBounds windFieldBounds;
        
        [ReadOnly] private float3 ori;
        [ReadOnly] public bool   super;
        
        //todo: 八叉树管理力场
        public void Execute()
        {
            var xPtr = (float*)X.GetUnsafePtr();
            var yPtr = (float*)Y.GetUnsafePtr();
            var zPtr = (float*)Z.GetUnsafePtr();

            var forcePtr = (PBDForceField*)ForceFields.GetUnsafeReadOnlyPtr();

            int3   dim      = math.select(this.dim, this.dim * 2, super);
            float3 maxRange = new float3(dim) - 1f;
            float3 ori      = math.select(this.ori, this.ori - 0.25f, super);
            float  scaleInv = math.select(1, 2, super);
            float  scale    = 1 / scaleInv;
            
            for (int i = 0; i < ForceFields.Length; i++)
            {
                var force  = forcePtr[i];
                var bounds = force.Bounds;
                if(!MathematicsUtil.AABBOverlap(bounds, windFieldBounds))
                    continue;

                //把bounds转到风场格子空间
                int3 Min = new (math.max(math.floor((bounds.Min - ori) * scaleInv), 0)),
                     Max = new (math.min(math.ceil((bounds.Max  - ori) * scaleInv), maxRange));

                int3 Size = Max - Min;

                for (int y = Min.y; y <= Max.y; y++)
                {
                    for (int z = Min.z; z <= Max.z; z++)
                    {
                        int3 pos = new int3(Min.x, y, z);

                        float3 wpos = new float3(pos) * scale + ori;

                        int index = GridUtils.GetIndex(pos, dim);
                        int end   = index + Size.x;

                        for (; index <= end; index++)
                        {
                            var result = force.CaculateForce(wpos, float3.zero);

                            (xPtr[index], yPtr[index], zPtr[index]) =
                                (xPtr[index] + result.x, yPtr[index] + result.y, zPtr[index] + result.z);

                            wpos.x += scale;
                        }
                    }
                }
                
            }

        }

        public void UpdateParams(ref NativeArray<float>        x,
                                 ref NativeArray<float>        y,
                                 ref NativeArray<float>        z,
                                 in  float3                    ori,
                                 in  PBDBounds                 fieldBounds,
                                 ref NativeList<PBDForceField> forceFields)
        {
            X               = x;
            Y               = y;
            Z               = z;
            this.ori        = ori;
            windFieldBounds = fieldBounds;
            ForceFields     = forceFields.AsParallelReader();
        }
    }

    
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct DownSampleJob : IJob
    {
        [WriteOnly, NativeDisableParallelForRestriction]
        private NativeArray<float> Data;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<float>.ReadOnly Super;

        [ReadOnly] public int3 dim;

        public void Execute()
        {
            var destPtr = (float*)Data.GetUnsafePtr();
            var srcPtr = (float*)Super.GetUnsafeReadOnlyPtr();

            // var stride = GridUtils.GetStride(dim);
            
            var superDim    = dim * 2;
            var superStride = GridUtils.GetStride(superDim);

            for (int y = 0; y < dim.y; y++)
            {
                for (int z = 0; z < dim.z; z++)
                {
                    int3 pos   = new int3(0, y, z);
                    int  index = GridUtils.GetIndex(pos, dim);
                    int  end   = index + dim.x;
                    
                    int3 min    = pos * 2;
                    int  sIndex = GridUtils.GetIndex(min, superDim);

                    for (; index < end; index++, sIndex += 2)
                    {
                        
                        float result = 0;
                        for (int sy = 0; sy <= 1; sy++)
                        {
                            for (int sz = 0; sz <= 1; sz++)
                            {
                                for (int sx = 0; sx <= 1; sx++)
                                {
                                    int sampleIndex = math.dot(superStride.xyz, new int3(sx, sy, sz)) + sIndex;
                                    result += srcPtr[sampleIndex];
                                }
                            }
                        }

                        result *= 0.125f;

                        destPtr[index] = result;
                        
                    }
                }
            }
        }

        public void UpdateParams(ref NativeArray<float> data,
                                 ref NativeArray<float> super,
                                 in  int3               dim)
        {
            Data     = data;
            Super    = super.AsReadOnly();
            this.dim = dim;
        }
    }
}