﻿//******************************************************************************************************
//  Downloader.cs - Gbtc
//
//  Copyright © 2016, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may not use this
//  file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  12/08/2015 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using DotRas;
using GSF;
using GSF.Configuration;
using GSF.Data.Model;
using GSF.IO;
using GSF.Scheduling;
using GSF.Threading;
using GSF.TimeSeries;
using GSF.TimeSeries.Adapters;
using GSF.Web.Model;
using openMIC.Model;

namespace openMIC
{
    public enum DirectoryNamingConvention
    {
        [Description("Top Folder")]
        Root,
        [Description("Year Sub-folder")]
        Year,
        [Description("YYYYMM Sub-folder")]
        YearMonth,
        [Description("Year/Month Sub-folders")]
        YearThenMonth
    }

    [Description("Downloader: Implements remote file download capabilities")]
    [EditorBrowsable(EditorBrowsableState.Advanced)] // Normally defined as an input device protocol
    public class Downloader : InputAdapterBase, IDevice
    {
        #region [ Members ]

        // Nested Types

        // Defines connection profile task settings
        public class ConnectionProfileTaskSettings
        {
            [ConnectionStringParameter,
            Description("Defines file names or patterns to download."),
            DefaultValue("*.*")]
            public string FileExtensions
            {
                get;
                set;
            }

            [ConnectionStringParameter,
            Description("Defines remote path to download files from ."),
            DefaultValue("/")]
            public string RemotePath
            {
                get;
                set;
            }

            [ConnectionStringParameter,
            Description("Defines local path to download files to."),
            DefaultValue("")]
            public string LocalPath
            {
                get;
                set;
            }

            [ConnectionStringParameter,
            Description("Determines if remote files should be deleted after download."),
            DefaultValue(false)]
            public bool DeleteRemoteFilesAfterDownload
            {
                get;
                set;
            }

            [ConnectionStringParameter,
            Description("Determines if total remote files to download should be limited by age."),
            DefaultValue(false)]
            public bool LimitRemoteFileDownloadByAge
            {
                get;
                set;
            }

            [ConnectionStringParameter,
            Description("Determines if old local files should be deleted."),
            DefaultValue(false)]
            public bool DeleteOldLocalFiles
            {
                get;
                set;
            }

            [ConnectionStringParameter,
            Description("Determines if existing local files should be overwritten."),
            DefaultValue(false)]
            public bool OverwriteExistingLocalFiles
            {
                get;
                set;
            }

            [ConnectionStringParameter,
            Description("Determines if existing local files should be archived before they are overwritten."),
            DefaultValue(false)]
            public bool ArchiveLocalFilesBeforeOverwrite
            {
                get;
                set;
            }

            [ConnectionStringParameter,
            Description("Defines external operation application."),
            DefaultValue("")]
            public string ExternalOperation
            {
                get;
                set;
            }

            [ConnectionStringParameter,
            Description("Defines maximum file size to download."),
            DefaultValue(1000)]
            public int MaximumFileSize
            {
                get;
                set;
            }

            [ConnectionStringParameter,
            Description("Defines maximum file count to download."),
            DefaultValue(-1)]
            public int MaximumFileCount
            {
                get;
                set;
            }

            [ConnectionStringParameter,
            Description("Defines directory naming convention."),
            DefaultValue("Root")]
            public string DirectoryNamingConvention
            {
                get;
                set;
            }

            [ConnectionStringParameter,
            Description("Defines directory authentication user name."),
            DefaultValue("")]
            public string DirectoryAuthUserName
            {
                get;
                set;
            }

            [ConnectionStringParameter,
            Description("Defines directory authentication password."),
            DefaultValue("")]
            public string DirectoryAuthPassword
            {
                get;
                set;
            }
        }

        // Fields
        private readonly RasDialer m_rasDialer;
        private ConnectionProfileTaskSettings[] m_connectionProfileTaskSettings;
        private LogicalThreadOperation m_dialUpOperation;
        private long m_startConnectionTime;
        private long m_startDialUpTime;
        private bool m_disposed;

        #endregion
        
        #region [ Constructors ]

        public Downloader()
        {
            m_rasDialer = new RasDialer();
            m_rasDialer.Error += m_rasDialer_Error;
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets or sets connection host name or IP for transport.
        /// </summary>
        [ConnectionStringParameter,
        Description("Defines connection host name or IP for transport.")]
        public string ConnectionHostName
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets connection host user name for transport.
        /// </summary>
        [ConnectionStringParameter,
        Description("Defines connection host user name for transport."),
        DefaultValue("anonymous")]
        public string ConnectionUserName
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets connection password for transport.
        /// </summary>
        [ConnectionStringParameter,
        Description("Defines connection password for transport."),
        DefaultValue("anonymous")]
        public string ConnectionPassword
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets connection profile record ID.
        /// </summary>
        [ConnectionStringParameter,
        Description("Defines connection profile record ID."),
        DefaultValue(0)]
        public int ConnectionProfileID
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets download schedule.
        /// </summary>
        [ConnectionStringParameter,
        Description("Defines download schedule."),
        DefaultValue("* * * * *")]
        public string Schedule
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets flag that determines if this connection will use dial-up.
        /// </summary>
        [ConnectionStringParameter,
        Description("Determines if this connection will use dial-up."),
        DefaultValue(false)]
        public bool UseDialUp
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets dial-up entry name.
        /// </summary>
        [ConnectionStringParameter,
        Description("Defines dial-up entry name."),
        DefaultValue("")]
        public string DialUpEntryName
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets dial-up phone number.
        /// </summary>
        [ConnectionStringParameter,
        Description("Defines dial-up phone number."),
        DefaultValue("")]
        public string DialUpNumber
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets dial-up user name.
        /// </summary>
        [ConnectionStringParameter,
        Description("Defines dial-up user name."),
        DefaultValue("")]
        public string DialUpUserName
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets dial-up password.
        /// </summary>
        [ConnectionStringParameter,
        Description("Defines dial-up password."),
        DefaultValue("")]
        public string DialUpPassword
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets maximum retries for a dial-up connection.
        /// </summary>
        [ConnectionStringParameter,
        Description("Defines maximum retries for a dial-up connection."),
        DefaultValue(3)]
        public int DialUpRetries
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets timeout for a dial-up connection.
        /// </summary>
        [ConnectionStringParameter,
        Description("Defines timeout for a dial-up connection."),
        DefaultValue(90)]
        public int DialUpTimeout
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets total data quality errors of this <see cref="IDevice"/>.
        /// </summary>
        public long DataQualityErrors
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets total time quality errors of this <see cref="IDevice"/>.
        /// </summary>
        public long TimeQualityErrors
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets total device errors of this <see cref="IDevice"/>.
        /// </summary>
        public long DeviceErrors
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets total measurements received for this <see cref="IDevice"/> - in local context "successful connections" per day.
        /// </summary>
        public long MeasurementsReceived
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets total measurements expected to have been received for this <see cref="IDevice"/> - in local context "expected connections" per day.
        /// </summary>
        public long MeasurementsExpected
        {
            get
            {
                Schedule schedule = new Schedule(Schedule);

                // Check for scheduled days of week
                if (schedule.DaysOfWeekPart.Values.Contains((int)DateTime.UtcNow.DayOfWeek))
                {
                    // Check for scheduled months
                    if (schedule.MonthPart.Values.Contains(DateTime.UtcNow.Month))
                    {
                        // Check for scheduled days of month
                        if (schedule.DayPart.Values.Contains(DateTime.UtcNow.Day))
                        {
                            // Return expected downloads per day
                            return schedule.HourPart.Values.Count * schedule.MinutePart.Values.Count;
                        }

                        // Not a matching day of month - no downloads expected for today
                        return 0;
                    }

                    // Not a matching month - no downloads expected for today
                    return 0;
                }

                // Not a matching day of week - no downloads expected for today
                return 0;
            }
            set
            {
                // Ignoring updates
            }
        }

        /// <summary>
        /// Gets or sets total number of attempted connections.
        /// </summary>
        public long AttemptedConnections
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets total number of successful connections.
        /// </summary>
        public long SuccessfulConnections
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets total number of failed connections.
        /// </summary>
        public long FailedConnections
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets total number of attempted dial-ups.
        /// </summary>
        public long AttemptedDialUps
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets total number of successful dial-ups.
        /// </summary>
        public long SuccessfulDialUps
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets total number of failed dial-ups.
        /// </summary>
        public long FailedDialUps
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets total number of files downloaded.
        /// </summary>
        public long FilesDownloaded
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets total number of bytes downloaded.
        /// </summary>
        public long BytesDownloaded
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets total connected time, in ticks.
        /// </summary>
        public long TotalConnectedTime
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets total dial-up time, in ticks.
        /// </summary>
        public long TotalDialUpTime
        {
            get;
            set;
        }

        /// <summary>
        /// Gets flag that determines if the data input connects asynchronously.
        /// </summary>
        /// <remarks>
        /// Derived classes should return true when data input source is connects asynchronously, otherwise return false.
        /// </remarks>
        protected override bool UseAsyncConnect => false;

        /// <summary>
        /// Gets the flag indicating if this adapter supports temporal processing.
        /// </summary>
        public override bool SupportsTemporalProcessing => false;

        // Gets RAS connection state
        private RasConnectionState RasState => RasConnection.GetActiveConnections().FirstOrDefault(ras => ras.EntryName == DialUpEntryName)?.GetConnectionStatus()?.ConnectionState ?? RasConnectionState.Disconnected;

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="Downloader"/> object and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                try
                {
                    if (disposing)
                    {
                        DeregisterSchedule(this);

                        if ((object)m_rasDialer != null)
                        {
                            m_rasDialer.Error -= m_rasDialer_Error;
                            m_rasDialer.Dispose();
                        }
                    }
                }
                finally
                {
                    m_disposed = true;          // Prevent duplicate dispose.
                    base.Dispose(disposing);    // Call base class Dispose().
                }
            }
        }

        /// <summary>
        /// Initializes <see cref="Downloader"/>.
        /// </summary>
        public override void Initialize()
        {
            base.Initialize();
            ConnectionStringParser<ConnectionStringParameterAttribute> parser = new ConnectionStringParser<ConnectionStringParameterAttribute>();
            parser.ParseConnectionString(ConnectionString, this);

            using (DataContext context = new DataContext())
            {
                IEnumerable<ConnectionProfileTask> tasks = context.Table<ConnectionProfileTask>().QueryRecords(null, new RecordRestriction("ConnectionProfileID={0}", ConnectionProfileID));
                List<ConnectionProfileTaskSettings> connectionProfileTaskSettings = new List<ConnectionProfileTaskSettings>();

                foreach (ConnectionProfileTask task in tasks)
                {
                    ConnectionProfileTaskSettings settings = new ConnectionProfileTaskSettings();
                    parser.ParseConnectionString(task.Settings, settings);
                    connectionProfileTaskSettings.Add(settings);
                }

                m_connectionProfileTaskSettings = connectionProfileTaskSettings.ToArray();
            }

            RegisterSchedule(this);
        }

        /// <summary>
        /// Attempts to connect to data input source.
        /// </summary>
        protected override void AttemptConnection()
        {
            foreach (ConnectionProfileTaskSettings settings in m_connectionProfileTaskSettings)
            {
                string localPath = settings.LocalPath.ToNonNullString().Trim();

                if (localPath.StartsWith(@"\\") && !string.IsNullOrEmpty(settings.DirectoryAuthUserName) && !string.IsNullOrEmpty(settings.DirectoryAuthPassword))
                {
                    string[] userParts = settings.DirectoryAuthUserName.Split('\\');
                    string[] pathParts = localPath.Substring(2).Split('\\');

                    try
                    {
                        if (userParts.Length == 2 && pathParts.Length > 1)
                            FilePath.ConnectToNetworkShare($"\\\\{pathParts[0].Trim()}\\{pathParts[1].Trim()}\\", userParts[1].Trim(), settings.DirectoryAuthPassword.Trim(), userParts[0].Trim());
                        else
                            throw new InvalidOperationException($"UNC based local path \"{settings.LocalPath}\" or authentication user name \"{settings.DirectoryAuthUserName}\" is not in the correct format.");
                    }
                    catch (Exception ex)
                    {
                        OnProcessException(new InvalidOperationException($"Exception while authenticating UNC path \"{settings.LocalPath}\": {ex.Message}", ex));
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to disconnect from data input source.
        /// </summary>
        protected override void AttemptDisconnection()
        {
            // Just leaving UNC paths authenticated for the duration of service run-time since multiple
            // devices may share the same destination path
        }

        /// <summary>
        /// Gets a short one-line status of this adapter.
        /// </summary>
        /// <param name="maxLength">Maximum number of available characters for display.</param>
        /// <returns>
        /// A short one-line summary of the current status of this adapter.
        /// </returns>
        public override string GetShortStatus(int maxLength)
        {
            if (!Enabled)
                return "Downloading for is paused...".CenterText(maxLength);

            return $"Downloading enabled for schedule: {Schedule}".CenterText(maxLength);
        }

        private void ExecuteTasks()
        {
        }

        private bool ConnectDialUp()
        {
            if (!UseDialUp)
                return false;

            m_startDialUpTime = 0;
            DisconnectDialUp();

            try
            {
                if (RasState == RasConnectionState.Connected)
                    throw new InvalidOperationException($"Cannot connect to \"{DialUpEntryName}\": already connected.");

                OnStatusMessage("Initiating dial-up for \"{0}\"...", DialUpEntryName);
                AttemptedDialUps++;

                m_rasDialer.EntryName = DialUpEntryName;
                m_rasDialer.PhoneNumber = DialUpPassword;
                m_rasDialer.Timeout = DialUpTimeout;
                m_rasDialer.Credentials = new NetworkCredential(DialUpUserName, DialUpPassword);
                m_rasDialer.Dial();

                m_startDialUpTime = DateTime.UtcNow.Ticks;
                SuccessfulDialUps++;
                OnStatusMessage("Dial-up connected on \"{0}\"", DialUpEntryName);
                return true;
            }
            catch (Exception ex)
            {
                FailedDialUps++;
                OnProcessException(new InvalidOperationException($"Exception while attempting to dial entry \"{DialUpEntryName}\": {ex.Message}", ex));
                DisconnectDialUp();
            }

            return false;
        }

        private void DisconnectDialUp()
        {
            if (!UseDialUp)
                return;

            try
            {
                OnStatusMessage("Initiating hang-up for \"{0}\"", DialUpEntryName);
                RasConnection.GetActiveConnections().FirstOrDefault(ras => ras.EntryName == DialUpEntryName)?.HangUp();
            }
            catch (Exception ex)
            {
                OnProcessException(new InvalidOperationException($"Exception while attempting to hang-up \"{DialUpEntryName}\": {ex.Message}", ex));
            }

            if (m_startDialUpTime > 0)
            {
                Ticks dialUpConnectedTime = DateTime.UtcNow.Ticks - m_startDialUpTime;
                OnStatusMessage("Dial-up connected for {0}", dialUpConnectedTime.ToElapsedTimeString(2));
                m_startDialUpTime = 0;
                TotalDialUpTime += dialUpConnectedTime;
            }
        }

        private void m_rasDialer_Error(object sender, ErrorEventArgs e)
        {
            OnProcessException(e.GetException());
        }

        #endregion

        #region [ Static ]

        // Static Fields
        private static readonly ScheduleManager s_scheduleManager;
        private static readonly ConcurrentDictionary<string, Downloader> s_instances;
        private static readonly ConcurrentDictionary<string, LogicalThread> s_dialupScheduler;

        // Static Constructor
        static Downloader()
        {
            s_instances = new ConcurrentDictionary<string, Downloader>();
            s_dialupScheduler = new ConcurrentDictionary<string, LogicalThread>();
            s_scheduleManager = new ScheduleManager();
            s_scheduleManager.ScheduleDue += s_scheduleManager_ScheduleDue;
            s_scheduleManager.Start();
        }

        private static void s_scheduleManager_ScheduleDue(object sender, EventArgs<Schedule> e)
        {
            Schedule schedule = e.Argument;
            Downloader instance;

            if (s_instances.TryGetValue(schedule.Name, out instance))
            {
                if (instance.UseDialUp)
                    instance.m_dialUpOperation.RunOnceAsync();
                else
                    instance.ExecuteTasks();
            }
        }

        // Static Properties

        // Static Methods
        private static void RegisterSchedule(Downloader instance)
        {
            s_instances.TryAdd(instance.Name, instance);

            if (instance.UseDialUp)
            {
                // Make sure dial-up's using the same resource (i.e., modem) are executed synchronously
                LogicalThread thread = s_dialupScheduler.GetOrAdd(instance.DialUpEntryName, entryName => new LogicalThread());

                instance.m_dialUpOperation = new LogicalThreadOperation(thread, () => {
                    if (instance.ConnectDialUp())
                    {
                        instance.ExecuteTasks();
                        instance.DisconnectDialUp();
                    }
                });
            }

            s_scheduleManager.AddSchedule(instance.Name, instance.Schedule, $"Download schedule for \"{instance.Name}\"", true);
        }

        private static void DeregisterSchedule(Downloader instance)
        {
            s_scheduleManager.RemoveSchedule(instance.Name);
            s_instances.TryRemove(instance.Name, out instance);
        }

        #endregion
    }
}
