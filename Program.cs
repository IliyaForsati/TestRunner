using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Mono.Cecil;


namespace RTProSL_TestRunner
{
    /// <summary>
    /// This project is designed to run tests concurrently across multiple threads.
    /// It reads the number of threads from the user, prepares the test environment, 
    /// and then executes the test classes in parallel. Each thread processes a task 
    /// from a shared queue of test classes, executing them using the `dotnet test` command.
    /// Results are collected, printed with color coding to indicate success or failure,
    /// and the directory is cleaned up after the tests are completed.
    /// </summary>
    public class Program
    {
        private static int numberOfThreads; // Number of threads to run tests concurrently
        private static ThreadState[] threads;
        private static ConcurrentQueue<string> TestsQueue;

        // this one is for running from slution directory
        private static Dictionary<int, string> ProjectsDict = new Dictionary<int, string>(){
            { 1 , @".\..\RTProSL-Test\RTProSL-MSTest\bin\Debug\net7.0"},
            { 2 , @".\..\RTProSL-Test\RTProSL-MSTest-Reports\bin\Debug\net7.0"}
        };
        // this one is for running from debug directory
        //private static Dictionary<int, string> ProjectsDict = new Dictionary<int, string>(){
        //    { 1 , @".\..\..\..\RTProSL-MSTest\bin\Debug\net7.0"},
        //    { 2 , @".\..\..\..\RTProSL-MSTest-Reports\bin\Debug\net7.0"}
        //};

        private static int projectKey;
        

        public static void Main(string[] args)
        {
            // calculate total time
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // Main method responsible for setting up threads and managing the overall test execution process
            Console.Write("How many threads do you want to use?");
            numberOfThreads = int.Parse(Console.ReadLine());

            Console.WriteLine("Which project do you want to run?\n  1-RTProSL_MSTest\n  2-RTProSL_MSTest_Reports");
            projectKey = int.Parse(Console.ReadLine());

            threads = new ThreadState[numberOfThreads];

            var TestsLst = findTestClasses();
            TestsQueue = new ConcurrentQueue<string>(TestsLst);

            // print number of test classes
            Console.WriteLine(TestsLst.Count() + "test classes were found");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("------------------------------------------------------------------------\n");
            Console.ResetColor();

            for (int i = 0; i < numberOfThreads; i++)
            {
                int id = i;

                threads[i] = new ThreadState();

                string dirName = $"thread#{id + 1}_results";
                Process.Start("cmd.exe", $@"/c robocopy {ProjectsDict[projectKey]} {dirName} /E").WaitForExit();
                threads[i].Dir = Path.Combine(dirName, "RTProSL-MSTest.dll");

                threads[i].Thread = new Thread(() => WorkerLoop(id));
                threads[i].Thread.Start();
            }

            for (int i = 0; i < numberOfThreads; i++)
            {
                threads[i].Thread.Join();
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"Thread #{i + 1}: Done :)");
                Console.ResetColor();

                // delete the created directory
                string directory = Path.GetDirectoryName(threads[i].Dir);
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }


            // stop timer
            stopwatch.Stop();
            string formattedTime = stopwatch.Elapsed.ToString(@"hh\:mm\:ss");

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"\nexecution time: {formattedTime}");
            Console.ResetColor();
        }

        /// <summary>
        /// Worker loop that continuously processes tasks from the queue until it's empty
        /// </summary>
        private static void WorkerLoop(int threadId)
        {
            // This method processes tasks from the queue and handles execution of tests.
            while (true)
            {
                if (TestsQueue.TryDequeue(out string task))
                {
                    lock (threads[threadId].Lock)
                    {
                        threads[threadId].Status = $"processing on <{task}>";
                    }

                    try
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"Thread #{threadId + 1}: start " + task);
                        Console.ResetColor();

                        string command = $"dotnet test {threads[threadId].Dir} --filter {task} -v=normal --logger trx;LogFileName={task}.trx";
                        TestRunner(command, threads[threadId], threadId);
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

        /// <summary>
        /// Finds all test classes in the given directory and returns them in a list
        /// </summary>
        private static List<string> findTestClasses()
        {
            var dllPath = Path.Combine(ProjectsDict[projectKey], "RTProSL-MSTest.dll");
            var assembly = AssemblyDefinition.ReadAssembly(dllPath);

            List<string> tests = new List<string>();

            foreach (var module in assembly.Modules)
            {
                foreach (var type in module.Types)
                {
                    foreach (var attr in type.CustomAttributes)
                    {
                        if (attr.AttributeType.Name == "TestClassAttribute")
                        {
                            tests.Add(type.FullName);
                        }
                    }
                }
            }

            return tests;
        }

        /// <summary>
        /// Executes the given test command and collects output and error streams
        /// </summary>
        private static void TestRunner(string command, ThreadState threadState, int threadId)
        {
            // This method runs the test command and handles the collection of output and error messages.
            lock (threadState.Lock)
            {
                threadState.Output.Clear();
            }

            lock (threadState.Lock)
            {
                threadState.Error.Clear();
            }

            using (Process proc = new Process())
            {
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

                proc.OutputDataReceived += (s, e) => { if (e.Data != null) lock (threadState.Lock) { threadState.Output.AppendLine("  " + e.Data); } };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (threadState.Lock) { threadState.Error.AppendLine("  " + e.Data); } };

                proc.EnableRaisingEvents = true;

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();
            }

            lock (threadState.Lock)
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"Thread #{threadId + 1}: End of {threadState.Status}\n");
                Console.ResetColor();

                ColoredPrinter(threadState.Output);
                ColoredPrinter(threadState.Error);
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("------------------------------------------------------------------------");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Prints the output and error results with color coding based on pass/fail status
        /// </summary>
        private static void ColoredPrinter(StringBuilder str)
            {
                // This method prints the results with color coding based on success (pass) or failure (fail)
                Console.OutputEncoding = Encoding.UTF8;
                foreach (var line in str.ToString().Split('\n'))
                {
                    foreach (var word in line.Split(' '))
                    {
                        if (Regex.IsMatch(word, @"\bpass(ed)?:?\b", RegexOptions.IgnoreCase))
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write($"✅ {word} ");
                        }
                        else if (Regex.IsMatch(word, @"\bfail(ed)?:?\b", RegexOptions.IgnoreCase))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Write($"❌ {word} ");
                        }
                        else if (Regex.IsMatch(word, @"\bresults:?\b", RegexOptions.IgnoreCase))
                        {
                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.Write($"📊 {word} ");
                        }
                        else
                        {
                            Console.ResetColor();
                            Console.Write(word + " ");
                        }
                    }
                    Console.WriteLine();
                }

                Console.ResetColor();
            }
    }

    // Class to store the state of each thread
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
