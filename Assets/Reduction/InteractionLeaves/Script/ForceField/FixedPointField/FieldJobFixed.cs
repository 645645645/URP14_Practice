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
    internal unsafe struct AddSourceJobFixed : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<VInt> Back;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Source;

        [ReadOnly] private VInt deltaTime;

        public void Execute()
        {
            var srcPtr  = (VInt*)Source.GetUnsafeReadOnlyPtr();
            var destPtr = (VInt*)Back.GetUnsafePtr();

            for (int i = 0; i < Back.Length; i++)
                destPtr[i] += srcPtr[i] * deltaTime;
        }

        public void Execute(int startIndex, int count)
        {
            int end = count + startIndex;

            var srcPtr  = (VInt*)Source.GetUnsafeReadOnlyPtr();
            var destPtr = (VInt*)Back.GetUnsafePtr();

            for (int x = startIndex; x < end; x++)
                destPtr[x] += srcPtr[x] * deltaTime;
        }

        public void UpdateParams(ref NativeArray<VInt> back, ref NativeArray<VInt> source, VInt deltaTime)
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
    internal unsafe struct DiffuseLineJobFixed : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<VInt> Back;

        [ReadOnly]
        private NativeArray<VInt>.ReadOnly Front;

        [ReadOnly] public int3 dim;
        [ReadOnly] public int3 N;
        [ReadOnly] public int4  stride;
        [ReadOnly] public int  iterateDirection;

        // [ReadOnly] private float diffusion; //[0,1]
        // [ReadOnly] private float deltaTime;

        [ReadOnly] private ConvolutionMethod convolutionMethod;

        [ReadOnly] private VInt2 divAndAbsorption;//预计算

        [ReadOnly] private int iterateCount;

        [ReadOnly] private int b;

        public void Execute()
        {
            (VInt div, VInt absorption) = (divAndAbsorption.x, divAndAbsorption.y);

            var srcPtr  = (VInt*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (VInt*)Back.GetUnsafePtr();
            
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
                                
                                VInt left = destPtr[index - stride.x],
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
                                
                                VInt left = destPtr[index - stride.z],
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

                                VInt left = destPtr[index - stride.y],
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
            
            (VInt div, VInt absorption) = (divAndAbsorption.x, divAndAbsorption.y);

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

            var srcPtr  = (VInt*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (VInt*)Back.GetUnsafePtr();

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

                    VInt left = destPtr[index - stride[iterateDirection]],
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

        public void UpdateParams(ref NativeArray<VInt> back,             ref NativeArray<VInt> front,
                                 VInt2                divAndAbsorption, ConvolutionMethod     convolution,
                                 int                   iterateCount = 1, int                   b = 0)
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
    internal unsafe struct DiffuseRBGSJobFixed : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<VInt> Back;

        [ReadOnly]
        private NativeArray<VInt>.ReadOnly Front;

        [ReadOnly] public int3 dim;
        [ReadOnly] public int3 N;
        [ReadOnly] public int4 stride;
        
        [ReadOnly] private VInt2 divAndAbsorption;

        [ReadOnly] private int iterateCount;

        [ReadOnly] private int  b;
        
        [ReadOnly] public  bool IsRedPhase; // 当前是红点还是黑点阶段

        public void Execute()
        {
            (VInt div, VInt absorption) = (divAndAbsorption.x, divAndAbsorption.y);

            var srcPtr  = (VInt*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (VInt*)Back.GetUnsafePtr();

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
                                VInt newValue = srcPtr[index] * div +
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
            (VInt div, VInt absorption) = (divAndAbsorption.x, divAndAbsorption.y);

            int yz = startIndex / count;
            int z  = yz         % N.z;
            int y  = yz         / N.z;

            bool isRed = ((y + z) & 1) == 0;
            
            int3 pos   = new int3(0, y, z);

            int  index = GridUtils.GetIndex(pos + 1, dim);

            int end = index + count;

            index = math.select(index, index + 1, isRed == IsRedPhase);

            var srcPtr  = (VInt*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (VInt*)Back.GetUnsafePtr();

            for (; index < end; index += 2)
            {
                VInt newValue = srcPtr[index] * div +
                                (destPtr[index - stride.x] + destPtr[index + stride.x] +
                                 destPtr[index - stride.y] + destPtr[index + stride.y] +
                                 destPtr[index - stride.z] + destPtr[index + stride.z]) * absorption;

                destPtr[index] = newValue;
                // destPtr[index] = math.lerp(destPtr[index], newValue, 1.5f);//1.0-1.8
            }
        }

        public void UpdateParams(ref NativeArray<VInt> back, ref NativeArray<VInt> front,
                                 VInt2                  divAndAbsorption,
                                 int                   iterateCount = 1, int b = 0)
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
    internal unsafe struct SetBoundaryJobFixed : IJobParallelFor, IJob
    {
        [NativeDisableParallelForRestriction] private NativeArray<VInt> Data;

        [ReadOnly] public  int3 dim;
        [ReadOnly] public  int3 N;
        [ReadOnly] public  int4 stride;
        [ReadOnly] private int  b; // 边界类型 0密度  1x  2y  3z 速度

        public void Execute()
        {
            var ptr = (VInt*)Data.GetUnsafePtr();
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
        private VInt HandleDensityBoundary(int3 pos, int3 boxMinDis)
        {
            // 密度场: 取最近内部点值
            int3 innerPos                    = pos;
            if (boxMinDis.x == 0) innerPos.x = pos.x == 0 ? 1 : dim.x - 2;
            if (boxMinDis.y == 0) innerPos.y = pos.y == 0 ? 1 : dim.y - 2;
            if (boxMinDis.z == 0) innerPos.z = pos.z == 0 ? 1 : dim.z - 2;

            return Data[GridUtils.GetIndex(innerPos, dim)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private VInt HandleVelocityBoundary(int3 pos, int3 boxMinDis)
        {
            VInt value      = 0;
            int  components = 0;

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
                       2 => value / 2,
                       3 => value / 3,
                       _ => 0
                   };
        }

        public void UpdateParams(ref NativeArray<VInt> data, int b)
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
    internal unsafe struct ReverseAdvectDensityJobFixed : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<VInt> Back;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Front;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly X;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Y;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Z;

        [ReadOnly] public int3  dim;
        [ReadOnly] public int3  N;
        [ReadOnly] public int4  stride;
        [ReadOnly] public VInt3 maxRange;
        
        [ReadOnly] private const int min = int.MinValue / 2;
        [ReadOnly] private const int max = int.MaxValue >> 1;

        [ReadOnly] private VInt deltaTime;
        [ReadOnly] private VInt ratio;

        public void Execute()
        {
            var srcPtr  = (VInt*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (VInt*)Back.GetUnsafePtr();
            var xPtr    = (VInt*)X.GetUnsafeReadOnlyPtr();
            var yPtr    = (VInt*)Y.GetUnsafeReadOnlyPtr();
            var zPtr    = (VInt*)Z.GetUnsafeReadOnlyPtr();

            VInt3 dt0 = new (deltaTime * N.x, deltaTime * N.y, deltaTime * N.z);

            for (int y = 1; y <= N.y; y++)
            {
                for (int z = 1; z <= N.z; z++)
                {
                    int3 gridPos = new int3(1, y, z);

                    VInt3 pos = FixedPointUtils.IntToFixed(gridPos);

                    int index = GridUtils.GetIndex(gridPos, dim);
                    int end   = index + N.x;
                    
                    for (; index < end; index++)
                    {
                        VInt3 vel = new VInt3(xPtr[index], yPtr[index], zPtr[index]);

                        VInt3 prePos = pos - dt0 * vel;

                        prePos = FixedPointUtils.Clamp(prePos, FixedPointUtils.half, maxRange);

                        VInt result = GridUtils.TrilinearStandard(prePos, ref srcPtr, dim) * ratio;

                        GridUtils.AtomicAddSat(ref destPtr, index, result.i, min, max);

                        pos.x += FixedPointUtils.ScaleInv;
                    }
                }
            }
            
        }

        public void Execute(int startIndex, int count)
        {
            var srcPtr  = (VInt*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (VInt*)Back.GetUnsafePtr();
            var xPtr    = (VInt*)X.GetUnsafeReadOnlyPtr();
            var yPtr    = (VInt*)Y.GetUnsafeReadOnlyPtr();
            var zPtr    = (VInt*)Z.GetUnsafeReadOnlyPtr();
            
            int3  gridPos = GridUtils.GetGridPos(startIndex, N) + 1;
            
            VInt3 pos = FixedPointUtils.IntToFixed(gridPos);

            VInt3 dt0 = new (deltaTime * N.x, deltaTime * N.y, deltaTime * N.z);

            int index = GridUtils.GetIndex(gridPos, dim);
            int end   = index + count;

            // int3 dir = new int3(1, 0, 0);
            
            for (; index < end; index++)
            {
                VInt3 vel = new VInt3(xPtr[index], yPtr[index], zPtr[index]);

                VInt3 prePos = pos - dt0 * vel;

                prePos = FixedPointUtils.Clamp(prePos, FixedPointUtils.half, maxRange);

                VInt result = GridUtils.TrilinearStandard(prePos, ref srcPtr, dim) * ratio;

                // (destPtr[index], pos.x) =  (result, pos.x + FixedPointUtils.ScaleInv);

                GridUtils.AtomicAddSat(ref destPtr, index, result.i, min, max);

                pos.x += FixedPointUtils.ScaleInv;
            }
        }

        public void UpdateParams(ref NativeArray<VInt> back,      
                                 ref NativeArray<VInt> front, 
                                 ref NativeArray<VInt> x, 
                                 ref NativeArray<VInt> y, 
                                 ref NativeArray<VInt> z,
                                 VInt                  deltaTime,
                                 VInt                  ratio)
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
    internal unsafe struct ForwardAdvectDensityJobFixed : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<VInt> Back;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Front;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly X;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Y;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Z;

        [ReadOnly] public int3  dim;
        [ReadOnly] public int3  N;
        [ReadOnly] public int4  stride;
        [ReadOnly] public VInt3 maxRange;
        
        [ReadOnly] private const int min = int.MinValue / 2;
        [ReadOnly] private const int max = int.MaxValue >> 1;

        [ReadOnly] private VInt deltaTime;
        [ReadOnly] private VInt ratio;

        public void Execute()
        {
            var srcPtr  = (VInt*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (VInt*)Back.GetUnsafePtr();
            var xPtr    = (VInt*)X.GetUnsafeReadOnlyPtr();
            var yPtr    = (VInt*)Y.GetUnsafeReadOnlyPtr();
            var zPtr    = (VInt*)Z.GetUnsafeReadOnlyPtr();

            VInt3 dt0 = new (deltaTime * N.x, deltaTime * N.y, deltaTime * N.z);

            // VInt3 dir   = new VInt3(1f, 0f, 0f);

            for (int y = 1; y <= N.y; y++)
            {
                for (int z = 1; z <= N.z; z++)
                {
                    int3 gridPos = new int3(1, y, z);

                    VInt3 pos = FixedPointUtils.IntToFixed(gridPos);

                    int index = GridUtils.GetIndex(gridPos, dim);
                    int end   = index + N.x;
                    
                    for (; index < end; index++)
                    {
                        VInt3 vel = new VInt3(xPtr[index], yPtr[index], zPtr[index]);

                        VInt energy = srcPtr[index] * ratio;

                        VInt3 prePos = pos + dt0 * vel;

                        prePos = FixedPointUtils.Clamp(prePos, FixedPointUtils.half, maxRange);

                        GridUtils.TrilinearStandardAtomicWrite(prePos, ref destPtr, dim,
                                                               energy, min, max);

                        pos.x += FixedPointUtils.ScaleInv;
                    }
                }
            }
            
        }

        public void Execute(int startIndex, int count)
        {
            var srcPtr  = (VInt*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (VInt*)Back.GetUnsafePtr();
            var xPtr    = (VInt*)X.GetUnsafeReadOnlyPtr();
            var yPtr    = (VInt*)Y.GetUnsafeReadOnlyPtr();
            var zPtr    = (VInt*)Z.GetUnsafeReadOnlyPtr();
            
            int3  gridPos = GridUtils.GetGridPos(startIndex, N) + 1;
            
            VInt3 pos = FixedPointUtils.IntToFixed(gridPos);

            VInt3 dt0 = new (deltaTime * N.x, deltaTime * N.y, deltaTime * N.z);

            int index = GridUtils.GetIndex(gridPos, dim);
            int end   = index + count;

            // int3 dir = new int3(1, 0, 0);
            
            for (; index < end; index++)
            {
                VInt3 vel = new VInt3(xPtr[index], yPtr[index], zPtr[index]);

                VInt energy = srcPtr[index] * ratio;

                VInt3 prePos = pos - dt0 * vel;

                prePos = FixedPointUtils.Clamp(prePos, FixedPointUtils.half, maxRange);
                
                
                GridUtils.TrilinearStandardAtomicWrite(prePos, ref destPtr, dim,
                                                       energy, min, max);

                pos.x += FixedPointUtils.ScaleInv;
            }
        }

        public void UpdateParams(ref NativeArray<VInt> back,      
                                 ref NativeArray<VInt> front, 
                                 ref NativeArray<VInt> x, 
                                 ref NativeArray<VInt> y, 
                                 ref NativeArray<VInt> z,
                                 VInt                  deltaTime,
                                 VInt                  ratio)
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

    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast,
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  // Debug = true,
                  DisableSafetyChecks = true)]
    internal unsafe struct ReverseAdvectVelocityJobFixed : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<VInt> BackX;

        [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<VInt> BackY;
        
        [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<VInt> BackZ;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly X;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Y;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Z;

        [ReadOnly] public int3 dim;
        [ReadOnly] public int3 N;
        [ReadOnly] public int4 stride;
        [ReadOnly] public VInt3 maxRange;

        [ReadOnly] private const int min = int.MinValue / 2;
        [ReadOnly] private const int max = int.MaxValue >> 1;

        [ReadOnly] private VInt deltaTime;
        [ReadOnly] private VInt ratio;

        public void Execute()
        {
            var dxPtr = (VInt*)BackX.GetUnsafePtr();
            var dyPtr = (VInt*)BackY.GetUnsafePtr();
            var dzPtr = (VInt*)BackZ.GetUnsafePtr();
            var xPtr  = (VInt*)X.GetUnsafeReadOnlyPtr();
            var yPtr  = (VInt*)Y.GetUnsafeReadOnlyPtr();
            var zPtr  = (VInt*)Z.GetUnsafeReadOnlyPtr();

            VInt3 dt0 = new (deltaTime * N.x, deltaTime * N.y, deltaTime * N.z);

            // int3 dir = new int3(1, 0, 0);

            for (int y = 1; y <= N.y; y++)
            {
                for (int z = 1; z <= N.z; z++)
                {
                    int3 gridPos = new int3(1, y, z);

                    VInt3 pos = FixedPointUtils.IntToFixed(gridPos);

                    int index = GridUtils.GetIndex(gridPos, dim);
                    int end   = index + N.x;

                    for (; index < end; index++)
                    {
                        VInt3 vel = new VInt3(xPtr[index], yPtr[index], zPtr[index]);

                        VInt3 prePos = pos - dt0 * vel;//反向

                        prePos = FixedPointUtils.Clamp(prePos, FixedPointUtils.half, maxRange);

                        VInt3 result = GridUtils.TrilinearStandard(prePos, ref xPtr, ref yPtr, ref zPtr, dim) * ratio;

                        GridUtils.AtomicAddSat(ref dxPtr, index, result.x, min, max);
                        GridUtils.AtomicAddSat(ref dyPtr, index, result.y, min, max);
                        GridUtils.AtomicAddSat(ref dzPtr, index, result.z, min, max);
                        
                        pos.x += FixedPointUtils.ScaleInv;
                    }
                }
            }
            
        }

        public void Execute(int startIndex, int count)
        {
            var dxPtr = (VInt*)BackX.GetUnsafePtr();
            var dyPtr = (VInt*)BackY.GetUnsafePtr();
            var dzPtr = (VInt*)BackZ.GetUnsafePtr();
            var xPtr  = (VInt*)X.GetUnsafeReadOnlyPtr();
            var yPtr  = (VInt*)Y.GetUnsafeReadOnlyPtr();
            var zPtr  = (VInt*)Z.GetUnsafeReadOnlyPtr();
            
            int3  gridPos = GridUtils.GetGridPos(startIndex, N) + 1;
            
            VInt3 pos = FixedPointUtils.IntToFixed(gridPos);

            VInt3 dt0 = new (deltaTime * N.x, deltaTime * N.y, deltaTime * N.z);

            int index = GridUtils.GetIndex(gridPos, dim);
            int end   = index + count;

            // int3 dir = new int3(1, 0, 0);
            
            for (; index < end; index++)
            {
                VInt3 vel = new VInt3(xPtr[index], yPtr[index], zPtr[index]);

                VInt3 prePos = pos - dt0 * vel;

                prePos = FixedPointUtils.Clamp(prePos, FixedPointUtils.half, maxRange);

                VInt3 result = GridUtils.TrilinearStandard(prePos, ref xPtr, ref yPtr, ref zPtr, dim) * ratio;

                GridUtils.AtomicAddSat(ref dxPtr, index, result.x, min, max);
                GridUtils.AtomicAddSat(ref dyPtr, index, result.y, min, max);
                GridUtils.AtomicAddSat(ref dzPtr, index, result.z, min, max);
                
                pos.x += FixedPointUtils.ScaleInv;
            }
        }

        public void UpdateParams(ref NativeArray<VInt> backx,      
                                 ref NativeArray<VInt> backy, 
                                 ref NativeArray<VInt> backz, 
                                 ref NativeArray<VInt> x, 
                                 ref NativeArray<VInt> y, 
                                 ref NativeArray<VInt> z,
                                 VInt                  deltaTime,
                                 VInt                  ratio)
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
                  // Debug = true,
                  DisableSafetyChecks = true)]
    internal unsafe struct ForwardAdvectVelocityJobFixed : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<VInt> BackX;

        [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<VInt> BackY;
        
        [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<VInt> BackZ;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly X;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Y;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Z;

        [ReadOnly] public int3 dim;
        [ReadOnly] public int3 N;
        [ReadOnly] public int4 stride;
        [ReadOnly] public VInt3 maxRange;
        
        [ReadOnly] private const int min = int.MinValue / 2;
        [ReadOnly] private const int max = int.MaxValue >> 1;

        [ReadOnly] private VInt deltaTime;
        [ReadOnly] private VInt ratio;

        public void Execute()
        {
            var dxPtr = (VInt*)BackX.GetUnsafePtr();
            var dyPtr = (VInt*)BackY.GetUnsafePtr();
            var dzPtr = (VInt*)BackZ.GetUnsafePtr();
            var xPtr  = (VInt*)X.GetUnsafeReadOnlyPtr();
            var yPtr  = (VInt*)Y.GetUnsafeReadOnlyPtr();
            var zPtr  = (VInt*)Z.GetUnsafeReadOnlyPtr();

            VInt3 dt0 = new (deltaTime * N.x, deltaTime * N.y, deltaTime * N.z);

            // int3 dir = new int3(1, 0, 0);

            for (int y = 1; y <= N.y; y++)
            {
                for (int z = 1; z <= N.z; z++)
                {
                    int3 gridPos = new int3(1, y, z);

                    VInt3 pos = FixedPointUtils.IntToFixed(gridPos);

                    int index = GridUtils.GetIndex(gridPos, dim);
                    int end   = index + N.x;

                    for (; index < end; index++)
                    {
                        VInt3 vel = new VInt3(xPtr[index], yPtr[index], zPtr[index]);
                        
                        VInt3 prePos = pos + dt0 * vel;//正向
                        
                        prePos = FixedPointUtils.Clamp(prePos, FixedPointUtils.half, maxRange);
                        
                        GridUtils.TrilinearStandardAtomicWrite(prePos, ref dxPtr, ref dyPtr, ref dzPtr,
                                                               dim, vel * ratio, min, max);
                        
                        pos.x += FixedPointUtils.ScaleInv;
                    }
                }
            }
            
        }

        public void Execute(int startIndex, int count)
        {
            var dxPtr = (VInt*)BackX.GetUnsafePtr();
            var dyPtr = (VInt*)BackY.GetUnsafePtr();
            var dzPtr = (VInt*)BackZ.GetUnsafePtr();
            var xPtr  = (VInt*)X.GetUnsafeReadOnlyPtr();
            var yPtr  = (VInt*)Y.GetUnsafeReadOnlyPtr();
            var zPtr  = (VInt*)Z.GetUnsafeReadOnlyPtr();
            
            int3  gridPos = GridUtils.GetGridPos(startIndex, N) + 1;
            
            VInt3 pos = FixedPointUtils.IntToFixed(gridPos);

            VInt3 dt0 = new (deltaTime * N.x, deltaTime * N.y, deltaTime * N.z);

            int index = GridUtils.GetIndex(gridPos, dim);
            int end   = index + count;

            // int3 dir = new int3(1, 0, 0);
            
            for (; index < end; index++)
            {
                VInt3 vel = new VInt3(xPtr[index], yPtr[index], zPtr[index]);

                VInt3 prePos = pos + dt0 * vel;//正向
                        
                prePos = FixedPointUtils.Clamp(prePos, FixedPointUtils.half, maxRange);
                        
                GridUtils.TrilinearStandardAtomicWrite(prePos, ref dxPtr, ref dyPtr, ref dzPtr,
                                                       dim, vel * ratio, min, max);
                
                pos.x += FixedPointUtils.ScaleInv;
            }
        }

        public void UpdateParams(ref NativeArray<VInt> backx,      
                                 ref NativeArray<VInt> backy, 
                                 ref NativeArray<VInt> backz, 
                                 ref NativeArray<VInt> x, 
                                 ref NativeArray<VInt> y, 
                                 ref NativeArray<VInt> z,
                                 VInt                  deltaTime,
                                 VInt                 ratio)
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
    internal unsafe struct DivergenceJobFixed : IJobParallelForBatch, IJob
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly X;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Y;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Z;

        [WriteOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt> Pressure;

        [WriteOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt> Divergence;

        [ReadOnly] public int3 dim;
        
        [ReadOnly] public int3 N;
        
        [ReadOnly] public int4 stride;

        public void Execute()
        {
            // VInt3 h = -new VInt3(0.5f / N.x, 0.5f / N.y, 0.5f / N.z);
            VInt3 h = -new VInt3(1f,1f,1f) / (2 * N);
            
            var xPtr = (VInt*)X.GetUnsafeReadOnlyPtr();
            var yPtr = (VInt*)Y.GetUnsafeReadOnlyPtr();
            var zPtr = (VInt*)Z.GetUnsafeReadOnlyPtr();

            var divPtr = (VInt*)Divergence.GetUnsafePtr();
            var prePtr = (VInt*)Pressure.GetUnsafePtr();

            for (int y = 1; y <= N.y; y++)
            {
                for (int z = 1; z <= N.z; z++)
                {
                    int  index = GridUtils.GetIndex(1, y, z, dim);
                    for (int i = 0; i <= N.x; i++)
                    {
                        divPtr[index] = new VInt(h.x) * (xPtr[index + stride.x] - xPtr[index - stride.x]) +
                                        new VInt(h.y) * (yPtr[index + stride.y] - yPtr[index - stride.y]) +
                                        new VInt(h.z) * (zPtr[index + stride.z] - zPtr[index - stride.z]);
                
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
            // VInt3 h = -new VInt3(0.5f / N.x, 0.5f / N.y, 0.5f / N.z);
            VInt3 h = -new VInt3(1f,1f,1f) / (2 * N);

            int yz = startIndex / count;
            int z  = yz         % N.z;
            int y  = yz         / N.z;

            int3 pos   = new int3(0, y, z);
            int  index = GridUtils.GetIndex(pos + 1, dim);
            
            var xPtr = (VInt*)X.GetUnsafeReadOnlyPtr();
            var yPtr = (VInt*)Y.GetUnsafeReadOnlyPtr();
            var zPtr = (VInt*)Z.GetUnsafeReadOnlyPtr();

            var divPtr = (VInt*)Divergence.GetUnsafePtr();
            var prePtr = (VInt*)Pressure.GetUnsafePtr();
            
            for (int i = 0; i <  count; i++)
            {
                divPtr[index] = new VInt(h.x) * (xPtr[index + stride.x] - xPtr[index - stride.x]) +
                                new VInt(h.y) * (yPtr[index + stride.y] - yPtr[index - stride.y]) +
                                new VInt(h.z) * (zPtr[index + stride.z] - zPtr[index - stride.z]);
                
                prePtr[index] = 0;

                index += stride.x;
            }
        }

        public void UpdateParams(ref NativeArray<VInt> pressure, 
                                 ref NativeArray<VInt> divergence, 
                                 ref NativeArray<VInt> x, 
                                 ref NativeArray<VInt> y, 
                                 ref NativeArray<VInt> z)
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
    internal unsafe struct PressureJobFixed : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<VInt> Pressure;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Divergence;

        [ReadOnly] public int3 dim;
        
        [ReadOnly] public int3 N;
        
        [ReadOnly] public int4 stride;

        [ReadOnly] private int iterateCount;
        
        [ReadOnly] public bool IsRedPhase; 
        
        [ReadOnly] private static readonly VInt inv = new VInt(0.16666667f);

        public void Execute()
        {
            var divPtr = (VInt*)Divergence.GetUnsafeReadOnlyPtr();
            var prePtr = (VInt*)Pressure.GetUnsafePtr();

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
                } 
                while (readAlpha);

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

            var divPtr = (VInt*)Divergence.GetUnsafeReadOnlyPtr();
            var prePtr = (VInt*)Pressure.GetUnsafePtr();

            for (; index < end; index += 2)
            {
                prePtr[index] = (divPtr[index]            +
                                 prePtr[index - stride.x] + prePtr[index + stride.x] +
                                 prePtr[index - stride.y] + prePtr[index + stride.y] +
                                 prePtr[index - stride.z] + prePtr[index + stride.z]) * inv;
            }
        }
        
        public void UpdateParams(ref NativeArray<VInt> pressure, ref NativeArray<VInt> divergence, int iterate)
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
    internal unsafe struct CorrectVelocityJobFixed : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<VInt> X;

        [NativeDisableParallelForRestriction]
        private NativeArray<VInt> Y;

        [NativeDisableParallelForRestriction]
        private NativeArray<VInt> Z;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Pressure;

        [ReadOnly] public int3 dim;
        
        [ReadOnly] public int3 N;
        
        [ReadOnly] public int4 stride;

        public void Execute()
        {
            int3 H = - N / 2;
            
            var xPtr   = (VInt*)X.GetUnsafePtr();
            var yPtr   = (VInt*)Y.GetUnsafePtr();
            var zPtr   = (VInt*)Z.GetUnsafePtr();
            var prePtr = (VInt*)Pressure.GetUnsafeReadOnlyPtr();

            for (int y = 1; y <= N.y; y++)
            {
                for (int z = 1; z <= N.z; z++)
                {
                    int3 pos   = new int3(1, y, z);
                    
                    int  index = GridUtils.GetIndex(pos, dim);

                    for (int i = 0; i < N.x; i++)
                    {
                        xPtr[index] += (H.x * (prePtr[index + stride.x] - prePtr[index - stride.x]));
                        yPtr[index] += (H.y * (prePtr[index + stride.y] - prePtr[index - stride.y]));
                        zPtr[index] += (H.z * (prePtr[index + stride.z] - prePtr[index - stride.z]));
                
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
            int3 H = - N / 2;

            int yz = startIndex / count;
            int z  = yz         % N.z;
            int y  = yz         / N.z;

            int3 pos   = new int3(0, y, z);
            int  index = GridUtils.GetIndex(pos + 1, dim);
            
            var xPtr   = (VInt*)X.GetUnsafePtr();
            var yPtr   = (VInt*)Y.GetUnsafePtr();
            var zPtr   = (VInt*)Z.GetUnsafePtr();
            var prePtr = (VInt*)Pressure.GetUnsafeReadOnlyPtr();

            for (int i = 0; i < count; i++)
            {
                xPtr[index] += (H.x * (prePtr[index + stride.x] - prePtr[index - stride.x]));
                yPtr[index] += (H.y * (prePtr[index + stride.y] - prePtr[index - stride.y]));
                zPtr[index] += (H.z * (prePtr[index + stride.z] - prePtr[index - stride.z]));
                
                // index += stride.x;
                index ++;
            }
        }
        
        
        public void UpdateParams(ref NativeArray<VInt> pressure, 
                                 ref NativeArray<VInt> x, ref NativeArray<VInt> y, ref NativeArray<VInt> z)
        {
            X        = x;
            Y        = y;
            Z        = z;
            Pressure = pressure.AsReadOnly();
        }
    }
    // set bnt x y z


    //模拟结束 结果保存 下一帧其他系统取用
    [BurstCompile]
    internal unsafe struct WindSimulateSaveJobFixed : IJob
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Back;

        [WriteOnly, NativeDisableParallelForRestriction]
        private NativeArray<float> ExchangeData;

        [ReadOnly] public int size;

        public void Execute()
        {            
            var destPtr = (float*)ExchangeData.GetUnsafePtr();
            var srcPtr  = (VInt*)Back.GetUnsafeReadOnlyPtr();
            for (int x = 0; x < Back.Length; x++)
            {
                destPtr[x] = FixedPointUtils.Fixed2Float(srcPtr[x].i);
            }
        }

        public void UpdateParams(ref NativeArray<VInt> back, ref NativeArray<float> data)
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
    internal unsafe struct WriteDensityFieldJobFixed : IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<VInt> D;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDCustomColliderInfo>.ReadOnly Rigibodys;

        [ReadOnly] public int3   dim;
        [ReadOnly] public int3   N;
        [ReadOnly] public float3 maxRange;

        [ReadOnly] private PBDBounds windFieldBounds;
        [ReadOnly] private float3    ori;
        [ReadOnly] public  bool      super;

        public void Execute()
        {
            var denPtr = (VInt*)D.GetUnsafePtr();
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
                if (!MathematicsUtil.AABBOverlap(bounds, windFieldBounds))
                    continue;

                int3 Min = new (math.max(math.floor((bounds.Min - ori) * scaleInv), 0)),
                     Max = new (math.min(math.ceil((bounds.Max  - ori) * scaleInv), maxRange));
                
                int3 Size = Max - Min;

                for (int y = Min.y; y <= Max.y; y++)
                {
                    for (int z = Min.z; z <= Max.z; z++)
                    {
                        int3 pos = new int3(Min.x, y, z);

                        float3 wpos  = new float3(pos) * scale + ori;

                        int index = GridUtils.GetIndex(pos, dim);
                        int end   = index + Size.x;

                        for (; index <= end; index++)
                        {
                            var result = rigi.AddDensityValue(wpos);

                            denPtr[index] += new VInt(result);

                            wpos.x += scale;
                        }
                    }
                }
            }
        }
        
        public void UpdateParams(ref NativeArray<VInt>                 d,
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
                  DisableSafetyChecks = true)]
    internal unsafe struct WriteVelocityFieldJobFixed : IJob
    {
        [NativeDisableParallelForRestriction]
        private NativeArray<VInt> X;

        [NativeDisableParallelForRestriction]
        private NativeArray<VInt> Y;

        [NativeDisableParallelForRestriction]
        private NativeArray<VInt> Z;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<PBDForceField>.ReadOnly ForceFields;

        [ReadOnly] public int3   dim;
        [ReadOnly] public int3   N;
        [ReadOnly] public float3 maxRange;

        [ReadOnly] private PBDBounds windFieldBounds;

        [ReadOnly] private float3 ori;
        [ReadOnly] public  bool   super;
        
        //todo: 八叉树管理力场
        public void Execute()
        {
            var xPtr = (VInt*)X.GetUnsafePtr();
            var yPtr = (VInt*)Y.GetUnsafePtr();
            var zPtr = (VInt*)Z.GetUnsafePtr();

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

                            VInt3 fixedResult = new VInt3(result);

                            (xPtr[index], yPtr[index], zPtr[index]) =
                                (xPtr[index] + fixedResult.x, yPtr[index] + fixedResult.y, zPtr[index] + fixedResult.z);

                            wpos.x += scale;
                        }
                    }
                }
            }

        }

        public void UpdateParams(ref NativeArray<VInt>         x,
                                 ref NativeArray<VInt>         y,
                                 ref NativeArray<VInt>         z,
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
    internal unsafe struct DownSampleJobFixed : IJob
    {
        [WriteOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt> Data;

        [ReadOnly, NativeDisableParallelForRestriction]
        private NativeArray<VInt>.ReadOnly Super;

        [ReadOnly] public int3 dim;

        public void Execute()
        {
            var destPtr = (VInt*)Data.GetUnsafePtr();
            var srcPtr  = (VInt*)Super.GetUnsafeReadOnlyPtr();

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
                        
                        VInt result = 0;
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

                        result /= 8;

                        destPtr[index] = result;
                        
                    }
                }
            }
        }

        public void UpdateParams(ref NativeArray<VInt> data,
                                 ref NativeArray<VInt> super,
                                 in  int3              dim)
        {
            Data     = data;
            Super    = super.AsReadOnly();
            this.dim = dim;
        }
    }
}