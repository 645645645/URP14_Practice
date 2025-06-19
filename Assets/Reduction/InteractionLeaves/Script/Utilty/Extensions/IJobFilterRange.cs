using System;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Mathematics;

namespace Unity.Jobs
{
    /// <summary>
    /// 支持范围过滤的Job接口，扩展自IJobFilter
    /// </summary>
    [JobProducerType(typeof(JobFilterRangeExtensions.JobFilterRangeProducer<>))]
    public interface IJobFilterRange : IJobFilter
    {
    }


    public static class JobFilterRangeExtensions
    {
        internal struct JobFilterRangeProducer<T> where T : struct, IJobFilterRange
        {
            public struct JobWrapper
            {
                [NativeDisableParallelForRestriction] 
                public NativeList<int> outputIndices;

                [ReadOnly] public int filterStart;  // 读取范围起始
                [ReadOnly] public int writeStart;   // 写入起始位置(-1=Append)
                [ReadOnly] public int appendCount;  // 读取数量

                public T JobData;
            }


            internal static readonly SharedStatic<IntPtr> jobReflectionData =
                SharedStatic<IntPtr>.GetOrCreate<JobFilterRangeProducer<T>>();

            internal static void Initialize()
            {
                if (jobReflectionData.Data == IntPtr.Zero)
                    jobReflectionData.Data = JobsUtility.CreateJobReflectionData(
                        typeof(JobWrapper), typeof(T), (ExecuteJobFunction)Execute);
            }

            public delegate void ExecuteJobFunction(ref JobWrapper jobWrapper,           IntPtr        additionalPtr,
                                                    IntPtr         bufferRangePatchData, ref JobRanges ranges, int jobIndex);


            public static void Execute(ref JobWrapper jobWrapper,           IntPtr        additionalPtr,
                                       IntPtr         bufferRangePatchData, ref JobRanges ranges, int jobIndex)
            {
                ExecuteOverwrite(ref jobWrapper, bufferRangePatchData);
            }

            public static unsafe void ExecuteOverwrite(ref JobWrapper jobWrapper, IntPtr bufferRangePatchData)
            {
                int outputIndex = math.select(
                    falseValue: jobWrapper.writeStart,
                    trueValue: jobWrapper.outputIndices.Length,
                    test: jobWrapper.writeStart < 0);
                
                jobWrapper.outputIndices.Capacity = 
                    math.max(outputIndex + jobWrapper.appendCount, jobWrapper.outputIndices.Capacity);

                int* outputPtr = (int*)jobWrapper.outputIndices.GetUnsafePtr();
                int  start     = jobWrapper.filterStart;
                int  end       = start + jobWrapper.appendCount;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                JobsUtility.PatchBufferMinMaxRanges(bufferRangePatchData, UnsafeUtility.AddressOf(ref jobWrapper),
                                                    start, jobWrapper.appendCount);
                JobsUtility.PatchBufferMinMaxRanges(bufferRangePatchData, UnsafeUtility.AddressOf(ref jobWrapper),
                                                    outputIndex, jobWrapper.appendCount);
#endif
                for (int i = start; i != end; i++)
                {
                    if (jobWrapper.JobData.Execute(i))
                    {
                        outputPtr[outputIndex] = i;
                        outputIndex++;
                    }
                }

                jobWrapper.outputIndices.ResizeUninitialized(outputIndex);
            }
        }

        public static void EarlyJobInit<T>() where T : struct, IJobFilterRange
        {
            JobFilterRangeProducer<T>.Initialize();
        }

        static IntPtr GetReflectionData<T>() where T : struct, IJobFilterRange
        {
            JobFilterRangeProducer<T>.Initialize();
            var reflectionData = JobFilterRangeProducer<T>.jobReflectionData.Data;
            CollectionHelper.CheckReflectionDataCorrect<T>(reflectionData);
            return reflectionData;
        }

        public static unsafe JobHandle ScheduleAppend<T>(this T jobData,     NativeList<int> indices,
                                                         int    filterStart, int             filterLength, JobHandle dependsOn = new JobHandle())
            where T : struct, IJobFilterRange
        {
            return jobData.ScheduleAppendByRef(indices, filterStart, filterLength, dependsOn);
        }
        
        public static unsafe JobHandle ScheduleAppend<T>(this T jobData,     NativeList<int> indices,
                                                         int             filterLength, JobHandle dependsOn = new JobHandle())
            where T : struct, IJobFilterRange
        {
            return jobData.ScheduleAppendByRef(indices, filterLength: filterLength, dependsOn: dependsOn);
        }

        public static unsafe JobHandle ScheduleOverwrite<T>(this T jobData,     NativeList<int> indices,
                                                            int    filterStart, int             filterLength, int writeStart, JobHandle dependsOn = new JobHandle())
            where T : struct, IJobFilterRange
        {
            return jobData.ScheduleOverwriteByRef(indices, filterStart, filterLength, writeStart, dependsOn);
        }
        
        public static unsafe JobHandle ScheduleOverwrite<T>(this T jobData,     NativeList<int> indices,
                                                            int    filterStart, int             filterLength, JobHandle dependsOn = new JobHandle())
            where T : struct, IJobFilterRange
        {
            return jobData.ScheduleOverwriteByRef(indices, filterStart, filterLength, dependsOn);
        }
        
        public static unsafe JobHandle ScheduleOverwrite<T>(this T jobData,     NativeList<int> indices,
                                                            int             filterLength, JobHandle dependsOn = new JobHandle())
            where T : struct, IJobFilterRange
        {
            return jobData.ScheduleOverwriteByRef(indices, filterLength, dependsOn);
        }

        public static unsafe JobHandle ScheduleAppendByRef<T>(ref this T jobData,     NativeList<int> indices,
                                                              int        filterStart, int             filterLength, JobHandle dependsOn = new JobHandle())
            where T : struct, IJobFilterRange
        {
            JobFilterRangeProducer<T>.JobWrapper jobWrapper = new()
            {
                JobData       = jobData,
                outputIndices = indices,
                filterStart   = filterStart,
                writeStart    = -1,
                appendCount   = filterLength,
            };

            var scheduleParams = new JobsUtility.JobScheduleParameters(
                UnsafeUtility.AddressOf(ref jobWrapper),
                GetReflectionData<T>(),
                dependsOn,
                ScheduleMode.Single);
            return JobsUtility.Schedule(ref scheduleParams);
        }
        
        public static unsafe JobHandle ScheduleAppendByRef<T>(ref this T jobData,      NativeList<int> indices,
                                                              int        filterLength, JobHandle       dependsOn = new JobHandle())
            where T : struct, IJobFilterRange
        {
            JobFilterRangeProducer<T>.JobWrapper jobWrapper = new()
            {
                JobData       = jobData,
                outputIndices = indices,
                filterStart   = 0,
                writeStart    = -1,
                appendCount   = filterLength,
            };

            var scheduleParams = new JobsUtility.JobScheduleParameters(
                UnsafeUtility.AddressOf(ref jobWrapper),
                GetReflectionData<T>(),
                dependsOn,
                ScheduleMode.Single);
            return JobsUtility.Schedule(ref scheduleParams);
        }


        public static unsafe JobHandle ScheduleOverwriteByRef<T>(ref this T jobData,     NativeList<int> indices,
                                                                 int        filterStart, int             filterLength, int writeStart, JobHandle dependsOn = new JobHandle())
            where T : struct, IJobFilterRange
        {
            JobFilterRangeProducer<T>.JobWrapper jobWrapper = new()
            {
                JobData       = jobData,
                outputIndices = indices,
                filterStart   = filterStart,
                writeStart    = writeStart,
                appendCount   = filterLength,
            };

            var scheduleParams = new JobsUtility.JobScheduleParameters(
                UnsafeUtility.AddressOf(ref jobWrapper),
                GetReflectionData<T>(),
                dependsOn,
                ScheduleMode.Single);
            return JobsUtility.Schedule(ref scheduleParams);
        }

        public static unsafe JobHandle ScheduleOverwriteByRef<T>(ref this T jobData,     NativeList<int> indices,
                                                                 int        filterStart, int             filterLength, JobHandle dependsOn = new JobHandle())
            where T : struct, IJobFilterRange
        {
            JobFilterRangeProducer<T>.JobWrapper jobWrapper = new()
            {
                JobData       = jobData,
                outputIndices = indices,
                filterStart   = filterStart,
                writeStart    = 0,
                appendCount   = filterLength,
            };

            var scheduleParams = new JobsUtility.JobScheduleParameters(
                UnsafeUtility.AddressOf(ref jobWrapper),
                GetReflectionData<T>(),
                dependsOn,
                ScheduleMode.Single);
            return JobsUtility.Schedule(ref scheduleParams);
        }

        public static unsafe JobHandle ScheduleOverwriteByRef<T>(ref this T jobData,      NativeList<int> indices,
                                                                 int        filterLength, JobHandle       dependsOn = new JobHandle())
            where T : struct, IJobFilterRange
        {
            JobFilterRangeProducer<T>.JobWrapper jobWrapper = new()
            {
                JobData       = jobData,
                outputIndices = indices,
                filterStart   = 0,
                writeStart    = 0,
                appendCount   = filterLength,
            };

            var scheduleParams = new JobsUtility.JobScheduleParameters(
                UnsafeUtility.AddressOf(ref jobWrapper),
                GetReflectionData<T>(),
                dependsOn,
                ScheduleMode.Single);
            return JobsUtility.Schedule(ref scheduleParams);
        }
    }

    public unsafe struct BoolConditionFilter<T> : IJobFilterRange where T : unmanaged
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<T>.ReadOnly tags;

        [ReadOnly] public bool targetValue;

        public bool Execute(int index)
        {
            bool value = UnsafeUtility.As<T, bool>(ref UnsafeUtility.ArrayElementAsRef<T>(tags.GetUnsafeReadOnlyPtr<T>(), index));
            return value == targetValue;
        }
    }
}