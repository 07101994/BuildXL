﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Demo
{
    /// <summary>
    /// Enumerates and read files under a sandbox, potentially blocking accesses under given directories
    /// </summary>
    public class BlockingEnumerator
    {
        private readonly LoggingContext m_loggingContext;
        private readonly PathTable m_pathTable;

        /// <nodoc/>
        public BlockingEnumerator(PathTable pathTable)
        {
            m_loggingContext = new LoggingContext(nameof(BlockingEnumerator));
            m_pathTable = pathTable;
        }

        /// <summary>
        /// Enumerates the given directory under the sandbox. Accesses under specified directories are blocked.
        /// </summary>
        public Task<SandboxedProcessResult> EnumerateWithBlockedDirectories(AbsolutePath directoryToEnumerate, IEnumerable<AbsolutePath> directoriesToBlock)
        {
            var directoryToEnumerateAsString = directoryToEnumerate.ToString(m_pathTable);

            // Enumerates all files and read them
            string pathToProcess;
            string arguments;
            if (OperatingSystemHelper.IsUnixOS)
            {
                pathToProcess = "/usr/bin/find";
                arguments = ". -type f -exec /bin/cat {} \\;";
            }
            else
            {
                pathToProcess = Environment.GetEnvironmentVariable("COMSPEC");
                arguments = "/C for /r %f in (*) do type %~ff";
            }

            var info =
                    new SandboxedProcessInfo(
                        m_pathTable,
                        new SimpleSandboxedProcessFileStorage(directoryToEnumerateAsString),
                        pathToProcess,
                        CreateManifest(AbsolutePath.Create(m_pathTable, pathToProcess), directoriesToBlock),
                        disableConHostSharing: true,
                        loggingContext: m_loggingContext)
                    {
                        Arguments = arguments,
                        WorkingDirectory = directoryToEnumerateAsString,
                        PipSemiStableHash = 0,
                        PipDescription = "EnumerateWithBlockedDirectories",
                        SandboxedKextConnection = OperatingSystemHelper.IsUnixOS ? new SandboxedKextConnection(numberOfKextConnections: 2) : null
                    };

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();

            return process.GetResultAsync();
        }

        /// <summary>
        /// The manifest is configured so all accesses under the provided collection of directories to block are blocked
        /// </summary>
        private FileAccessManifest CreateManifest(AbsolutePath pathToProcess, IEnumerable<AbsolutePath> directoriesToBlock)
        {
            var fileAccessManifest = new FileAccessManifest(m_pathTable)
            {
                FailUnexpectedFileAccesses = true,
                ReportFileAccesses = true,
                MonitorChildProcesses = true,
            };

            // We allow all file accesses at the root level, so by default everything is allowed
            fileAccessManifest.AddScope(AbsolutePath.Invalid, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowAll);

            // We explicitly allow reading from the tool path
            fileAccessManifest.AddPath(pathToProcess, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowRead);

            // We block access on all provided directories
            foreach (var directoryToBlock in directoriesToBlock)
            {
                fileAccessManifest.AddScope(
                    directoryToBlock,
                    FileAccessPolicy.MaskAll,
                    FileAccessPolicy.Deny & FileAccessPolicy.ReportAccess);
            }
            
            return fileAccessManifest;
        }
    }
}
