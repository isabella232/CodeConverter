﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using ICSharpCode.CodeConverter;
using ICSharpCode.CodeConverter.CSharp;
using ICSharpCode.CodeConverter.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.VisualBasic.FileIO;
using Xunit;
using SearchOption = System.IO.SearchOption;

namespace CodeConverter.Tests
{
    /// <summary>
    /// For all files in the testdata folder relevant to the testname, ensures they match the result of the conversion.
    /// Any extra files generated by the conversion are ignored.
    /// </summary>
    public class ProjectConverterTestBase
    {
        /// <summary>
        /// Leave this set to false when committing.
        /// Turn it on to manually check the output loads in VS.
        /// Commit only the modified files.
        /// </summary>
        private bool _writeNewCharacterization = false;

        public void ConvertProjectsWhere<TLanguageConversion>(Func<Project, bool> shouldConvertProject, [CallerMemberName] string testName = "") where TLanguageConversion : ILanguageConversion, new()
        {
            using (var workspace = MSBuildWorkspace.Create())
            {
                var originalSolutionDir = Path.Combine(GetTestDataDirectory(), "CharacterizationTestSolution");
                var solutionFile = Path.Combine(originalSolutionDir, "CharacterizationTestSolution.sln");

                var solution = workspace.OpenSolutionAsync(solutionFile).GetAwaiter().GetResult();
                var languageNameToConvert = typeof(TLanguageConversion) == typeof(VBToCSConversion)
                    ? LanguageNames.VisualBasic
                    : LanguageNames.CSharp;
                var projectsToConvert = solution.Projects.Where(p => p.Language == languageNameToConvert && shouldConvertProject(p)).ToArray();
                var conversionResults = SolutionConverter.CreateFor<TLanguageConversion>(projectsToConvert).Convert().ToDictionary(c => c.TargetPathOrNull);
                var expectedResultDirectory = GetExpectedResultDirectory<TLanguageConversion>(testName);

                try {
                    var expectedFiles = expectedResultDirectory.GetFiles("*", SearchOption.AllDirectories);
                    AssertAllExpectedFilesAreEqual(expectedFiles, conversionResults, expectedResultDirectory, originalSolutionDir);
                    AssertAllConvertedFilesWereExpected(expectedFiles, conversionResults, expectedResultDirectory, originalSolutionDir);
                    AssertNoConversionErrors(conversionResults);
                } finally {
                    if (_writeNewCharacterization) {
                        if (expectedResultDirectory.Exists) expectedResultDirectory.Delete(true);
                        //FileSystem.CopyDirectory(originalSolutionDir, expectedResultDirectory.FullName); //Uncomment to copy all source files too so the sln is runnable

                        foreach (var conversionResult in conversionResults) {
                            var expectedFilePath =
                                conversionResult.Key.Replace(originalSolutionDir, expectedResultDirectory.FullName);
                            Directory.CreateDirectory(Path.GetDirectoryName(expectedFilePath));
                            File.WriteAllText(expectedFilePath, conversionResult.Value.ConvertedCode);
                        }
                    }
                } 

            }

            Assert.False(_writeNewCharacterization, $"Test setup issue: Set {_writeNewCharacterization} to false after using it");
        }

        private static void AssertAllConvertedFilesWereExpected(FileInfo[] expectedFiles,
            Dictionary<string, ConversionResult> conversionResults, DirectoryInfo expectedResultDirectory,
            string originalSolutionDir)
        {
            AssertSubset(expectedFiles.Select(f => f.FullName.Replace(expectedResultDirectory.FullName, "")), conversionResults.Select(r => r.Key.Replace(originalSolutionDir, "")));
        }

        private void AssertAllExpectedFilesAreEqual(FileInfo[] expectedFiles, Dictionary<string, ConversionResult> conversionResults,
            DirectoryInfo expectedResultDirectory, string originalSolutionDir)
        {
            foreach (var expectedFile in expectedFiles)
            {
                AssertFileEqual(conversionResults, expectedResultDirectory, expectedFile, originalSolutionDir);
            }
        }

        private static void AssertNoConversionErrors(Dictionary<string, ConversionResult> conversionResults)
        {
            var errors = conversionResults
                .SelectMany(r => (r.Value.Exceptions ?? new string[0]).Select(e => new {Path = r.Key, Exception = e}))
                .ToList();
            Assert.Empty(errors);
        }

        private static void AssertSubset(IEnumerable<string> superset, IEnumerable<string> subset)
        {
            var notExpected = new HashSet<string>(subset, StringComparer.OrdinalIgnoreCase);
            notExpected.ExceptWith(new HashSet<string>(superset, StringComparer.OrdinalIgnoreCase));
            Assert.Empty(notExpected);
        }

        private void AssertFileEqual(Dictionary<string, ConversionResult> conversionResults,
            DirectoryInfo expectedResultDirectory,
            FileInfo expectedFile,
            string actualSolutionDir)
        {
            var convertedFilePath = expectedFile.FullName.Replace(expectedResultDirectory.FullName, actualSolutionDir);
            var fileDidNotNeedConversion = !conversionResults.ContainsKey(convertedFilePath) && File.Exists(convertedFilePath);
            if (fileDidNotNeedConversion) return;

            Assert.True(conversionResults.ContainsKey(convertedFilePath), expectedFile.Name + " is missing from the conversion result");

            var expectedText = Utils.HomogenizeEol(File.ReadAllText(expectedFile.FullName));
            var conversionResult = conversionResults[convertedFilePath];
            var actualText =
                Utils.HomogenizeEol(conversionResult.ConvertedCode ?? "" + conversionResult.GetExceptionsAsString() ?? "");

            Assert.Equal(expectedText, actualText);
            Assert.Equal(GetEncoding(expectedFile.FullName), GetEncoding(conversionResult));
        }

        private Encoding GetEncoding(ConversionResult conversionResult)
        {
            var filePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            conversionResult.TargetPathOrNull = filePath;
            conversionResult.WriteToFile();
            var encoding = GetEncoding(filePath);
            File.Delete(filePath);
            return encoding;
        }

        private static Encoding GetEncoding(string filePath)
        {
            using (var reader = new StreamReader(filePath, true)) {
                reader.Peek();
                return reader.CurrentEncoding;
            }
        }

        private static DirectoryInfo GetExpectedResultDirectory<TLanguageConversion>(string testName) where TLanguageConversion : ILanguageConversion, new()
        {
            var combine = Path.Combine(GetTestDataDirectory(), typeof(TLanguageConversion).Name, testName);
            return new DirectoryInfo(combine);
        }

        private static string GetTestDataDirectory()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var solutionDir = new FileInfo(new Uri(assembly.CodeBase).LocalPath).Directory?.Parent?.Parent?.Parent ??
                              throw new InvalidOperationException(assembly.CodeBase);
            return Path.Combine(solutionDir.FullName, "TestData");
        }
    }
}