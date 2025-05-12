using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;


namespace TestRunner
{
    public class Program
    {
        private static int numberOfThreads;
        // tuple of thread, status, dirOfThread, resonse of the tests
        private static ThreadState[] threads; 
        private static ConcurrentQueue<string> TestsQueue; // it's a safe queue for multi threading

        public static void Main(string[] args)
        {
            numberOfThreads = int.Parse(Console.ReadLine());
            threads = new ThreadState[numberOfThreads];

            var TestsLst = findTestClasses();
            TestsQueue = new ConcurrentQueue<string>(TestsLst);

            // know we want to run test on some threads
            for (int i = 0; i < numberOfThreads; i++)
            {
                int id = i;

                threads[i] = new ThreadState();

                string dirName = $"thread#{id}-results";
                Process.Start("cmd.exe", $@"/c robocopy C:\Users\Afshin\Desktop\project\RTProSL-Test\RTProSL-MSTest\bin\Debug\net7.0 {dirName} /E").WaitForExit();
                threads[i].Dir = Path.Combine(dirName, "RTProSL-MSTest.dll");

                threads[i].Thread = new Thread(() => WorkerLoop(id));
                threads[i].Thread.Start();
            }

            // show threads status in console
            //while (true)
            //{
            //    Thread.Sleep(5000);

            //    Console.Clear();
            //    for (int i = 0; i < numberOfThreads; i++)
            //    {
            //        lock (threads[i].Lock)
            //        {
            //            Console.WriteLine($"Thread #{i + 1}: {threads[i].Status}\n");
            //            Console.WriteLine(threads[i].Output);
            //            Console.WriteLine(threads[i].Error);
            //            Console.WriteLine("----------------");
            //        }
            //    }

            //    if (AllThreadsFinished())
            //        break;
            //}
            
        }

        private static void WorkerLoop(int threadId) 
        {
            while (true)
            {
                if (TestsQueue.TryDequeue(out string task))
                {
                    lock (threads[threadId].Lock)
                    {
                        threads[threadId].Status = $"Processing: {task}";
                    }

                    try
                    {
                        string command = $"dotnet test {threads[threadId].Dir} --filter {task} -v=normal --logger trx;LogFileName={task}.trx";
                        TestRunner(command, threads[threadId].Output, threads[threadId].Error, threads[threadId].Lock);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"An error occurred while running test '{task}': {ex.Message}");
                    }
                }
                else
                {
                    lock (threads[threadId].Lock)
                    {
                        threads[threadId].Status = "Idle";
                    }
                    break;
                }
            }

            lock (threads[threadId].Lock)
            {
                threads[threadId].Status = "Finished";
            }
        }

        private static bool AllThreadsFinished()
        {
            foreach (var thread in threads)
            {
                lock (thread.Lock)
                {
                    if (thread.Status != "Finished")
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        ///  find all of namespace of test classes and save them in a data structure (List)
        /// </summary>
        private static List<string> findTestClasses() 
        {
            // Path to your project root directory
            //string rootDir = @"E:\test\New\RTProSL-Test\RTProSL-MSTest"; // <-- Change this to your project path
            string rootDir = @"C:\Users\Afshin\Desktop\project\RTProSL-Test"; // <-- Change this to your project path
            List<string> testClasses = new List<string>();

            // Get all .cs files recursively
            foreach (var file in Directory.GetFiles(rootDir, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                string namespaceName = "";
                string className = "";
                bool hasTestClassAttribute = false;

                // Regex to find namespace
                Regex namespaceRegex = new Regex(@"^\s*namespace\s+([\w\.]+)");
                // Regex to find class
                Regex classRegex = new Regex(@"^\s*(public|private|internal|protected|static|final|abstract)?\s*class\s+(\w+)");
                // Regex to identify [TestClass] attribute
                Regex testClassAttributeRegex = new Regex(@"^\s*\[TestClass\]");

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    // Check for [TestClass] attribute
                    if (testClassAttributeRegex.IsMatch(line))
                    {
                        hasTestClassAttribute = true;
                    }

                    // Check for namespace
                    Match nsMatch = namespaceRegex.Match(line);
                    if (nsMatch.Success)
                    {
                        namespaceName = nsMatch.Groups[1].Value;
                    }

                    // Check for class
                    Match classMatch = classRegex.Match(line);
                    if (classMatch.Success)
                    {
                        className = classMatch.Groups[2].Value;
                        // Once class is found, stop searching
                        break;
                    }
                }

                if (hasTestClassAttribute && !string.IsNullOrEmpty(className))
                {
                    if (!string.IsNullOrEmpty(namespaceName))
                    {
                        testClasses.Add($"{namespaceName}.{className}");
                    }
                    else
                    {
                        testClasses.Add(className); // Global namespace
                    }
                }
            }

            return testClasses;
        }

        private static void TestRunner(string command, StringBuilder output, StringBuilder error, object threadLock)
        {
            // reset response
            lock (threadLock)
            {
                output.Clear();
            }

            lock (threadLock)
            {
                error.Clear();
            }

            Process proc = new Process();

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            proc.StartInfo = psi;

            proc.OutputDataReceived += (s, e) => { if (e.Data != null) lock (threadLock) { output.AppendLine(e.Data);}};
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (threadLock) { error.AppendLine(e.Data); } };

            proc.EnableRaisingEvents = true;

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();

            lock (threadLock)
            {
                Console.WriteLine(output);
                Console.WriteLine(error);
                Console.WriteLine("------------------");
            }
        }
    }

    internal class ThreadState
    {
        public Thread Thread { get; set; }
        public string Status { get; set; }
        public string Dir { get; set; }
        public StringBuilder Output = new StringBuilder();
        public StringBuilder Error = new StringBuilder();
        public object Lock = new object();
    }

}