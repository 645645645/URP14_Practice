using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    [BurstCompile(FloatPrecision.Low, FloatMode.Default)]
    internal unsafe struct AddSourceJob : IJobParallelForBatch
    {
        [NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float> Back;

        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float>.ReadOnly Source;

        [ReadOnly] private float deltaTime;
        
        [ReadOnly] private bool stopNan;

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
    
    //如果接受足够多的随机混合成的"稳定" 是可以转高斯-塞德尔方式的
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct DiffuseXJob : IJobParallelForBatch, IJob
    {
        [NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float> Back;

        [ReadOnly, NativeDisableUnsafePtrRestriction]
        private NativeArray<float>.ReadOnly Front;

        [ReadOnly] public int3 dim;
        [ReadOnly] public int3 N;

        [ReadOnly] private float diffusion; //[0,1]
        [ReadOnly] private float deltaTime;

        [ReadOnly] private ConvolutionMethod convolutionMethod;

        [ReadOnly] private float2 divAndAbsorption;//预计算

        public void Execute()
        {
            (float div, float absorption) = (divAndAbsorption.x, divAndAbsorption.y);
            
        }

        //输入范围[0，dim-2]即[0, N],需要在三个方向映射偏移一步
        public void Execute(int startIndex, int count)
        {
            // float a = deltaTime * diffusion * (N.x * N.y * N.z);
            //
            // float div        = 1 / (1 + 2 * a);
            // float absorption = a * div;
            
            (float div, float absorption) = (divAndAbsorption.x, divAndAbsorption.y);

            int yz = startIndex / count;
            int z  = yz         % N.z;
            int y  = yz         / N.z;
            
            int3 pos   = new int3(0, y, z);

            int  index = GridUtils.GetIndex(pos + 1, dim);

            var srcPtr  = (float*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (float*)Back.GetUnsafePtr();

            switch (convolutionMethod)
            {
                case ConvolutionMethod.LineSequence:

                    for (int i = 0; i < count; i++)
                    {
                        
                        destPtr[index] = srcPtr[index] * div +
                                         (destPtr[index - 1] +
                                          destPtr[index + 1]) * absorption;
                    
                        index++;
                    }
                    break;
                
                case ConvolutionMethod.LineSlidingWindow:

                    float left = destPtr[index - 1],
                          mid  = destPtr[index], 
                          right;

                    for (int i = 0; i < count; i++)
                    {
                        right = destPtr[index + 1];

                        destPtr[index] = srcPtr[index] * div + (left + right) * absorption;

                        (left, mid) = (mid, right);

                        index++;
                    }
                    break;
            }
        }

        public void UpdateParams(ref NativeArray<float> back, ref NativeArray<float> front, 
                                 float      diff, float      deltaTime, 
                                 float2     divAndAbsorption, 
                                 ConvolutionMethod convolution)
        {
            Back                  = back;
            Front                 = front.AsReadOnly();
            diffusion             = diff;
            this.deltaTime        = deltaTime;
            this.divAndAbsorption = divAndAbsorption;
            convolutionMethod     = convolution;
        }
    }
    
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct DiffuseYJob : IJobParallelForBatch
    {
        [NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float> Back;

        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float>.ReadOnly Front;

        [ReadOnly] public int3 dim;
        [ReadOnly] public int3 N;

        [ReadOnly] private float  diffusion; //[0,1]
        [ReadOnly] private float  deltaTime;

        [ReadOnly] private ConvolutionMethod convolutionMethod;
        
        [ReadOnly] private float2 divAndAbsorption;

        public void Execute(int startIndex, int count)
        {
            // float a = deltaTime * diffusion * (N.x * N.y * N.z);
            //
            // float div        = 1 / (1 + 2 * a);
            // float absorption = a * div;

            (float div, float absorption) = (divAndAbsorption.x, divAndAbsorption.y);

            int xz = startIndex / count;

            int x = xz % N.x,
                z = xz / N.x;

            int3 pos = new int3(x, 0 ,z);
            
            int  index = GridUtils.GetIndex(pos + 1, dim);
            
            int4 stride = GridUtils.GetStride(dim);

            var srcPtr  = (float*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (float*)Back.GetUnsafePtr();

            switch (convolutionMethod)
            {
                case ConvolutionMethod.LineSequence:

                    for (int i = 0; i < count; i++)
                    {
                        destPtr[index] = srcPtr[index] * div +
                                         (destPtr[index - stride.y] +
                                          destPtr[index + stride.y]) * absorption;
                        
                        index += stride.y;
                    }
                    
                    break;
                case ConvolutionMethod.LineSlidingWindow:
            
                    float left = destPtr[index - stride.y],
                          mid  = destPtr[index], 
                          right;

                    for (int i = 0; i < count; i++)
                    {
                        right = destPtr[index + stride.y];

                        destPtr[index] = srcPtr[index] * div + (left + right) * absorption;

                        (left, mid) = (mid, right);

                        index += stride.y;
                    }
                    break;
            }
        }

        public void UpdateParams(ref NativeArray<float>        back, ref NativeArray<float> front,
                                 float             diff, float      deltaTime, 
                                 float2            divAndAbsorption, 
                                 ConvolutionMethod convolution)
        {
            Back                  = back;
            Front                 = front.AsReadOnly();
            diffusion             = diff;
            this.deltaTime        = deltaTime;
            this.divAndAbsorption = divAndAbsorption;
            convolutionMethod     = convolution;
        }
    }
    
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct DiffuseZJob : IJobParallelForBatch
    {
        [NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        public NativeArray<float> Back;

        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        public NativeArray<float>.ReadOnly Front;

        [ReadOnly] public int3 dim;
        [ReadOnly] public int3 N;

        [ReadOnly] private float diffusion; //[0,1]
        [ReadOnly] private float deltaTime;

        [ReadOnly] private ConvolutionMethod convolutionMethod;

        [ReadOnly] private float2 divAndAbsorption;
        
        public void Execute(int startIndex, int count)
        {
            // float a = deltaTime * diffusion * (N.x * N.y * N.z);
            //
            // float div        = 1 / (1 + 2 * a);
            // float absorption = a * div;
            
            (float div, float absorption) = (divAndAbsorption.x, divAndAbsorption.y);

            int xy = startIndex / count;
            int x  = xy         % N.x;
            int y  = xy         / N.x;
            
            int3 pos = new int3(x, y, 0);

            int  index  = GridUtils.GetIndex(pos + 1, dim);
            int4 stride = GridUtils.GetStride(dim);

            var srcPtr  = (float*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (float*)Back.GetUnsafePtr();

            switch (convolutionMethod)
            {
                case ConvolutionMethod.LineSequence:
                    
                    for (int i = 0; i < count; i++)
                    {
                        destPtr[index] = srcPtr[index] * div +
                                         (destPtr[index - stride.z] +
                                          destPtr[index + stride.z]) * absorption;
                        
                        index += stride.z;
                    }
                    break;
                case ConvolutionMethod.LineSlidingWindow:
                    
                    float left = destPtr[index - stride.z],
                          mid  = destPtr[index], 
                          right;

                    for (int i = 0; i < count; i++)
                    {
                        right = destPtr[index + stride.z];

                        destPtr[index] = srcPtr[index] * div + (left + right) * absorption;

                        (left, mid) = (mid, right);

                        index += stride.z;
                    }
                    break;
            }
        }

        public void UpdateParams(ref NativeArray<float>        back, ref NativeArray<float> front, 
                                 float             diff, float      deltaTime, 
                                 float2            divAndAbsorption, 
                                 ConvolutionMethod convolution)
        {
            Back                  = back;
            Front                 = front.AsReadOnly();
            diffusion             = diff;
            this.deltaTime        = deltaTime;
            this.divAndAbsorption = divAndAbsorption;
            convolutionMethod     = convolution;
        }
    }

    /// <summary>
    /// length=6, batch=1
    /// 舒适,but 只分6个任务，实际时间又不如按行并行
    /// 为什么别的job为了省一个分支弄了三份变体，这个又妥协了
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct SetBoundaryParallelForJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] private NativeArray<float> Data;

        [ReadOnly] public  int3 dim;
        [ReadOnly] private int  b; // 边界类型 0密度  1x  2y  3z 速度
        
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
            // 处理每个边界方向
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
                  Debug = true,
                  DisableSafetyChecks = true)]
    internal unsafe struct AdvectJob : IJobParallelForBatch
    {
        [WriteOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<float> Back;

        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<float>.ReadOnly Front;

        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<float>.ReadOnly X;

        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<float>.ReadOnly Y;

        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction, NativeDisableContainerSafetyRestriction]
        private NativeArray<float>.ReadOnly Z;

        [ReadOnly] public  int3   dim;
        [ReadOnly] public  int3   N;

        [ReadOnly] private float deltaTime;
        
        [ReadOnly] private bool stopNan;

        public void Execute(int startIndex, int count)
        {
            var srcPtr  = (float*)Front.GetUnsafeReadOnlyPtr();
            var destPtr = (float*)Back.GetUnsafePtr();
            var xPtr    = (float*)X.GetUnsafeReadOnlyPtr();
            var yPtr    = (float*)Y.GetUnsafeReadOnlyPtr();
            var zPtr    = (float*)Z.GetUnsafeReadOnlyPtr();
            
            int3 pos = GridUtils.GetGridPos(startIndex, N) + 1;

            float3 dt0      = deltaTime * (float3)N,
                   maxRange = (float3)dim - 0.5f;

            int index = GridUtils.GetIndex(pos, dim);

            int3 dir = new int3(1, 0, 0);
            
            for (int i = 0; i < count; i++)
            {
                float3 vel = new float3(xPtr[index], yPtr[index], zPtr[index]);

                float3 prePos = pos - dt0 * vel;

                prePos = math.clamp(prePos, 0.5f, maxRange);


                float result = GridUtils.TrilinearStandard(prePos, ref srcPtr, dim);

                (destPtr[index], pos, index) = (result, pos + dir, index + 1);
            }
        }

        public void UpdateParams(ref NativeArray<float> back,      
                                 ref NativeArray<float> front, 
                                 ref NativeArray<float> x, 
                                 ref NativeArray<float> y, 
                                 ref NativeArray<float> z,
                                 float      deltaTime)
        {
            Back           = back;
            Front          = front.AsReadOnly();
            X              = x.AsReadOnly();
            Y              = y.AsReadOnly();
            Z              = z.AsReadOnly();
            this.deltaTime = deltaTime;
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct DivergenceJob : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float>.ReadOnly X;

        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float>.ReadOnly Y;

        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float>.ReadOnly Z;

        [WriteOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float> Pressure;

        [WriteOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float> Divergence;

        [ReadOnly] public int3 dim;
        
        [ReadOnly] public int3 N;

        public void Execute(int startIndex, int count)
        {
            float3 h = - 0.5f * MathematicsUtil.one / N;

            int yz = startIndex / count;
            int z  = yz         % N.z;
            int y  = yz         / N.z;

            int3 pos   = new int3(0, y, z);
            int  index = GridUtils.GetIndex(pos + 1, dim);
            
            int4 stride = GridUtils.GetStride(dim);
            
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
    

    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct PressureJob : IJobParallelForBatch
    {
        [NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float> Pressure;

        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float>.ReadOnly Divergence;

        [ReadOnly] public int3 dim;
        
        [ReadOnly] public int3 N;
        
        [ReadOnly] private const float inv = 0.16666667f;
        
        public void Execute(int startIndex, int count)
        {
            int yz = startIndex / count;
            int z  = yz         % N.z;
            int y  = yz         / N.z;

            int3 pos   = new int3(0, y, z);
            int  index = GridUtils.GetIndex(pos + 1, dim);

            int4 stride = GridUtils.GetStride(dim);

            var divPtr = (float*)Divergence.GetUnsafeReadOnlyPtr();
            var prePtr = (float*)Pressure.GetUnsafePtr();

            for (int i = 0; i < count; i++)
            {
                prePtr[index] = (divPtr[index]            +
                                 prePtr[index - stride.x] + prePtr[index + stride.x] +
                                 prePtr[index - stride.y] + prePtr[index + stride.y] +
                                 prePtr[index - stride.z] + prePtr[index + stride.z]) * inv;
                    
                // index += stride.x;
                index ++;
            }
        }
        
        public void UpdateParams(ref NativeArray<float> pressure, ref NativeArray<float> divergence)
        {
            Pressure   = pressure;
            Divergence = divergence.AsReadOnly();
        }
    }
    //set bnt p
    

    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct CorrectVelocityJob : IJobParallelForBatch
    {
        [NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float> X;

        [NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float> Y;

        [NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float> Z;
        
        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float>.ReadOnly Pressure;

        [ReadOnly] public int3 dim;
        
        [ReadOnly] public int3   N;
        
        [ReadOnly] private bool stopNan;
        
        public void Execute(int startIndex, int count)
        {
            float3 H = - 0.5f * (float3)N;

            int yz = startIndex / count;
            int z  = yz         % N.z;
            int y  = yz         / N.z;

            int3 pos   = new int3(0, y, z);
            int  index = GridUtils.GetIndex(pos + 1, dim);

            int4 stride = GridUtils.GetStride(dim);
            
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


    //模拟结束 结果保存 下一帧其他系统取用
    [BurstCompile]
    internal unsafe struct WindSimulateSaveJob : IJob
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float>.ReadOnly Back;

        [WriteOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
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
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  // Debug = true,
                  DisableSafetyChecks = true)]
    internal unsafe struct WriteDensityFieldJob : IJobParallelForBatch
    {
        [WriteOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float> D;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDCustomColliderInfo>.ReadOnly Rigibodys;
        
        [ReadOnly] public int3   dim;
        [ReadOnly] public int3   N;
        [ReadOnly] public float3 ori;
        
        public void Execute(int startIndex, int count)
        {
            int3 pos = GridUtils.GetGridPos(startIndex, dim);
            
            float3 wpos   = (pos + ori);

            var denPtr = (float*)D.GetUnsafePtr();
            var rigPtr = (PBDCustomColliderInfo*)Rigibodys.GetUnsafeReadOnlyPtr();

            for (int index = startIndex; index < startIndex + count; index++)
            {
                float result = 0;

                //密度应该不是这么加的吧
                for (int i = 0; i < Rigibodys.Length; i++)
                    result += rigPtr[i].AddDensityValue(wpos);

                denPtr[index] = result;

                wpos.x += 1;
            }
        }


        public void UpdateParams(ref NativeArray<float>                d,
                                 in  float3                            ori,
                                 ref NativeList<PBDCustomColliderInfo> rigibodys)
        {
            D         = d;
            this.ori  = ori;
            Rigibodys = rigibodys.AsParallelReader();
        }
    }
    
    
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    internal unsafe struct WriteVelocityFieldJob : IJobParallelForBatch
    {
        [WriteOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float> X;

        [WriteOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float> Y;

        [WriteOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        private NativeArray<float> Z;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDForceField>.ReadOnly ForceFields;

        [ReadOnly] public int3   dim;
        [ReadOnly] public int3   N;
        [ReadOnly] public float3 ori;
        
        public void Execute(int startIndex, int count)
        {
            int3 pos = GridUtils.GetGridPos(startIndex, dim);

            float3 wpos = (pos + ori);
            
            var xPtr = (float*)X.GetUnsafePtr();
            var yPtr = (float*)Y.GetUnsafePtr();
            var zPtr = (float*)Z.GetUnsafePtr();

            var forcePtr = (PBDForceField*)ForceFields.GetUnsafeReadOnlyPtr();
            
            for (int index = startIndex; index < startIndex + count; index++)
            {
                // float3 result = -velocity;
                float3 result = float3.zero;
                
                for (int j = 0; j < ForceFields.Length; j++)
                    result += forcePtr[j].CaculateForce(wpos, float3.zero);

                (xPtr[index], yPtr[index], zPtr[index]) =  (result.x, result.y, result.z);

                wpos.x  += 1;
            }
        }

        public void UpdateParams(ref NativeArray<float>        x,
                                 ref NativeArray<float>        y,
                                 ref NativeArray<float>        z,
                                 in  float3                    ori,
                                 ref NativeList<PBDForceField> forceFields)
        {
            X              = x;
            Y              = y;
            Z              = z;
            this.ori       = ori;
            ForceFields    = forceFields.AsParallelReader();
        }
    }
}