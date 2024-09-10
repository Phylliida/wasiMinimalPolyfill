

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Wasmtime;

namespace WasiMinimalPolyfill
{
    public class WasiFileSystem
    {
        // should return how many bytes written
        public delegate int WriteDataHandler(System.Span<byte> dataWritten, int bytesWritten);
        public event WriteDataHandler OnWriteStdOut;
        public event WriteDataHandler OnWriteStdErr;

        // should return how many bytes read
        public delegate int ReadDataHandler(System.Span<byte> memoryToWriteTo, int bytesRequested);
        public event ReadDataHandler OnReadStdIn;

        public event WasiMinimalPolyfill.WasmPolyfill.MemoryGrowthEvent OnMemoryGrowth;

        public Wasmtime.Memory memory;

        public Wasmtime.Instance instance;

        static string FS_WRAPPER = "fs_wrapper";

        public WasiFileSystem(Wasmtime.Engine engine, Wasmtime.Linker linker, Wasmtime.Store store, string fileSystemWasmPath)
        {
            Wasmtime.Module fileSystemModule = Wasmtime.Module.FromFile(engine, fileSystemWasmPath);
            
            linker.Define(
                FS_WRAPPER,
                "read_stdin",
                Function.FromCallback(store, (Int32 buf, Int32 len, Int64 offset) =>
                {
                    System.Span<byte> dataSpan = memory.GetSpan(buf, len + (Int32)offset).Slice((Int32)offset);
                    return OnReadStdIn(dataSpan, len);
                })
            );

            linker.Define(
                FS_WRAPPER,
                "write_stdout",
                Function.FromCallback(store, (Int32 buf, Int32 len, Int64 offset) =>
                {
                    System.Span<byte> dataSpan = memory.GetSpan(buf, len + (Int32)offset).Slice((Int32)offset);
                    return OnWriteStdOut(dataSpan, len);
                })
            );

            linker.Define(
                FS_WRAPPER,
                "write_stderr",
                Function.FromCallback(store, (Int32 buf, Int32 len, Int64 offset) =>
                {
                    System.Span<byte> dataSpan = memory.GetSpan(buf, len + (Int32)offset).Slice((Int32)offset);
                    return OnWriteStdErr(dataSpan, len);
                })
            );

            WasmPolyfill.AttachClockTimeExport(linker, store, () => memory);
            WasmPolyfill.AttachMemoryGrowthExport(linker, store, OnMemoryGrowth);

            instance = linker.Instantiate(store, fileSystemModule);

            memory = instance.GetMemory("memory");

            instance.GetFunction("_initialize").Invoke();
        }



    }

    public class WasmPolyfill
    {
        public delegate Wasmtime.Memory GetMemory();

        public delegate void MemoryGrowthEvent(Int32 growthAmount);
        public static void AttachMemoryGrowthExport(Wasmtime.Linker linker, Wasmtime.Store store, MemoryGrowthEvent growthEvent = null)
        {
            linker.Define(
                "env",
                "emscripten_notify_memory_growth",
                Function.FromCallback(store, (Int32 growthAmount) =>
                {
                    if (growthEvent != null)
                    {
                        growthEvent(growthAmount);
                    }
                })
            );
        }
        private static long nanoTime()
        {
            long nano = 10000L * Stopwatch.GetTimestamp();
            nano /= TimeSpan.TicksPerMillisecond;
            nano *= 100L;
            return nano;
        }

        // from https://stackoverflow.com/a/44136515
        public static void AttachClockTimeExport(Wasmtime.Linker linker, Wasmtime.Store store, GetMemory memoryGetter, string packageName = "")
        {
            linker.Define(
                packageName,
                "__wasi_clock_time_get",
                Function.FromCallback(store, (Int32 clockId, Int64 precision, Int32 pointerToOutput) =>
                {
                    memoryGetter().WriteInt64(pointerToOutput, nanoTime());
                })
            );
        }
    }
}