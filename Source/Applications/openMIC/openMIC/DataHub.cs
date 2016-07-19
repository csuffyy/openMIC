﻿//******************************************************************************************************
//  DataHub.cs - Gbtc
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
//  01/14/2016 - Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using GSF;
using GSF.Data.Model;
using GSF.Identity;
using GSF.Web.Model;
using GSF.Web.Security;
using Microsoft.AspNet.SignalR;
using openMIC.Model;
using RecordRestriction = GSF.Data.Model.RecordRestriction;

namespace openMIC
{
    [AuthorizeHubRole]
    public class DataHub : Hub, IRecordOperationsHub
    {
        #region [ Members ]

        // Fields
        private readonly DataContext m_dataContext;
        private ModbusHubClient m_modbusHubClient;
        private bool m_disposed;

        #endregion

        #region [ Constructors ]

        public DataHub()
        {
            m_dataContext = new DataContext();
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets <see cref="IRecordOperationsHub.RecordOperationsCache"/> for SignalR hub.
        /// </summary>
        public RecordOperationsCache RecordOperationsCache => s_recordOperationsCache;

        private ModbusHubClient ModbusHubClient
        {
            get
            {
                return m_modbusHubClient ?? (m_modbusHubClient = s_modbusHubClients.GetOrAdd(Context.ConnectionId, id => new ModbusHubClient(Clients.Client(Context.ConnectionId))));
            }
        }
        #endregion

        #region [ Methods ]

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="DataHub"/> object and optionally releases the managed resources.
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
                        m_dataContext?.Dispose();
                    }
                }
                finally
                {
                    m_disposed = true;          // Prevent duplicate dispose.
                    base.Dispose(disposing);    // Call base class Dispose().
                }
            }
        }

        public override Task OnConnected()
        {
            s_connectCount++;
            Program.Host.LogStatusMessage($"DataHub connect by {Context.User?.Identity?.Name ?? "Undefined User"} [{Context.ConnectionId}] - count = {s_connectCount}");
            return base.OnConnected();
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            if (stopCalled)
            {
                ModbusHubClient modbusHubClient;

                // Dispose of Modbus hub client when client connection is disconnected
                if (s_modbusHubClients.TryRemove(Context.ConnectionId, out modbusHubClient))
                    modbusHubClient.Dispose();

                m_modbusHubClient = null;
                s_connectCount--;

                Program.Host.LogStatusMessage($"DataHub disconnect by {Context.User?.Identity?.Name ?? "Undefined User"} [{Context.ConnectionId}] - count = {s_connectCount}");
            }

            return base.OnDisconnected(stopCalled);
        }

        #endregion

        #region [ Static ]

        // Static Properties

        /// <summary>
        /// Gets the hub connection ID for the current thread.
        /// </summary>
        public static string CurrentConnectionID => s_connectionID.Value;

        // Static Fields
        private static readonly ConcurrentDictionary<string, ModbusHubClient> s_modbusHubClients;
        private static volatile int s_connectCount;
        private static int s_downloaderProtocolID;
        private static readonly ThreadLocal<string> s_connectionID = new ThreadLocal<string>();
        private static readonly RecordOperationsCache s_recordOperationsCache;

        // Static Methods

        /// <summary>
        /// Gets statically cached instance of <see cref="RecordOperationsCache"/> for <see cref="DataHub"/> instances.
        /// </summary>
        /// <returns>Statically cached instance of <see cref="RecordOperationsCache"/> for <see cref="DataHub"/> instances.</returns>
        public static RecordOperationsCache GetRecordOperationsCache() => s_recordOperationsCache;

        // Static Constructor
        static DataHub()
        {
            s_modbusHubClients = new ConcurrentDictionary<string, ModbusHubClient>(StringComparer.OrdinalIgnoreCase);

            // Analyze and cache record operations of data hub
            s_recordOperationsCache = new RecordOperationsCache(typeof(DataHub));
            Downloader.ProgressUpdated += DownloaderProgressUpdated;
        }

        private static void DownloaderProgressUpdated(object sender, EventArgs<ProgressUpdate> e)
        {
            Downloader instance = sender as Downloader;

            if ((object)instance != null)
            {
                ProgressUpdate update = e.Argument;

                update.DeviceName = instance.Name;
                update.FilesDownloaded = instance.FilesDownloaded;

                if (!string.IsNullOrEmpty(update.ProgressMessage))
                    update.ProgressMessage += $"\r\n\r\n[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}]";

                GlobalHost.ConnectionManager.GetHubContext<DataHub>().Clients.All.deviceProgressUpdate(e.Argument);
            }
        }

        #endregion

        // Client-side script functionality

        #region [ Device Table Operations ]

        /// <summary>
        /// Gets protocol ID for "Downloader" adapter.
        /// </summary>
        public int DownloaderProtocolID => s_downloaderProtocolID != 0 ? s_downloaderProtocolID : (s_downloaderProtocolID = m_dataContext.Connection.ExecuteScalar<int>("SELECT ID FROM Protocol WHERE Acronym='Downloader'"));

        [RecordOperation(typeof(Device), RecordOperation.QueryRecordCount)]
        public int QueryDeviceCount(string filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
                return m_dataContext.Table<Device>().QueryRecordCount();

            return m_dataContext.Table<Device>().QueryRecordCount(new RecordRestriction("Acronym LIKE {0} OR Name LIKE {0}", $"%{filterText}%"));
        }

        [RecordOperation(typeof(Device), RecordOperation.QueryRecords)]
        public IEnumerable<Device> QueryDevices(string sortField, bool ascending, int page, int pageSize, string filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
                return m_dataContext.Table<Device>().QueryRecords(sortField, ascending, page, pageSize);

            return m_dataContext.Table<Device>().QueryRecords(sortField, ascending, page, pageSize, new RecordRestriction("Acronym LIKE {0} OR Name LIKE {0}", $"%{filterText}%"));
        }

        public IEnumerable<Device> QueryDevices(int limit, string filterText)
        {
            return m_dataContext.Table<Device>().QueryRecords("Acronym", new RecordRestriction("Enabled <> 0 AND (Acronym LIKE {0} OR Name LIKE {0})", $"%{filterText}%"), limit);
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(Device), RecordOperation.DeleteRecord)]
        public void DeleteDevice(int id)
        {
            m_dataContext.Table<Device>().DeleteRecord(id);
        }

        [RecordOperation(typeof(Device), RecordOperation.CreateNewRecord)]
        public Device NewDevice()
        {
            return new Device();
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(Device), RecordOperation.AddNewRecord)]
        public void AddNewDevice(Device device)
        {
            device.NodeID = Program.Host.Model.Global.NodeID;
            device.UniqueID = Guid.NewGuid();
            device.ProtocolID = DownloaderProtocolID;
            device.CreatedBy = GetCurrentUserID();
            device.CreatedOn = DateTime.UtcNow;
            device.UpdatedBy = device.CreatedBy;
            device.UpdatedOn = device.CreatedOn;

            if (string.IsNullOrWhiteSpace(device.OriginalSource))
                device.OriginalSource = device.Acronym;

            m_dataContext.Table<Device>().AddNewRecord(device);
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(Device), RecordOperation.UpdateRecord)]
        public void UpdateDevice(Device device)
        {
            device.ProtocolID = DownloaderProtocolID;
            device.UpdatedBy = device.CreatedBy;
            device.UpdatedOn = device.CreatedOn;

            if (string.IsNullOrWhiteSpace(device.OriginalSource))
                device.OriginalSource = device.Acronym;

            m_dataContext.Table<Device>().UpdateRecord(device);
        }

        #endregion

        #region [ ConnectionProfile Table Operations ]

        [RecordOperation(typeof(ConnectionProfile), RecordOperation.QueryRecordCount)]
        public int QueryConnectionProfileCount(string filterText)
        {
            return m_dataContext.Table<ConnectionProfile>().QueryRecordCount();
        }

        [RecordOperation(typeof(ConnectionProfile), RecordOperation.QueryRecords)]
        public IEnumerable<ConnectionProfile> QueryConnectionProfiles(string sortField, bool ascending, int page, int pageSize, string filterText)
        {
            return m_dataContext.Table<ConnectionProfile>().QueryRecords(sortField, ascending, page, pageSize);
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(ConnectionProfile), RecordOperation.DeleteRecord)]
        public void DeleteConnectionProfile(int id)
        {
            m_dataContext.Table<ConnectionProfile>().DeleteRecord(id);
        }

        [RecordOperation(typeof(ConnectionProfile), RecordOperation.CreateNewRecord)]
        public ConnectionProfile NewConnectionProfile()
        {
            return new ConnectionProfile();
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(ConnectionProfile), RecordOperation.AddNewRecord)]
        public void AddNewConnectionProfile(ConnectionProfile connectionProfile)
        {
            connectionProfile.CreatedBy = GetCurrentUserID();
            connectionProfile.CreatedOn = DateTime.UtcNow;
            connectionProfile.UpdatedBy = connectionProfile.CreatedBy;
            connectionProfile.UpdatedOn = connectionProfile.CreatedOn;

            m_dataContext.Table<ConnectionProfile>().AddNewRecord(connectionProfile);
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(ConnectionProfile), RecordOperation.UpdateRecord)]
        public void UpdateConnectionProfile(ConnectionProfile connectionProfile)
        {
            connectionProfile.UpdatedBy = connectionProfile.CreatedBy;
            connectionProfile.UpdatedOn = connectionProfile.CreatedOn;

            m_dataContext.Table<ConnectionProfile>().UpdateRecord(connectionProfile);
        }

        #endregion

        #region [ ConnectionProfileTask Table Operations ]

        public int QueryConnectionProfileTaskCount(int parentID)
        {
            return QueryConnectionProfileTaskCount(parentID, null);
        }

        [RecordOperation(typeof(ConnectionProfileTask), RecordOperation.QueryRecordCount)]
        public int QueryConnectionProfileTaskCount(int parentID, string filterText)
        {
            return m_dataContext.Table<ConnectionProfileTask>().QueryRecordCount(new RecordRestriction
            {
                FilterExpression = "ConnectionProfileID = {0}",
                Parameters = new object[] { parentID }
            });
        }

        [RecordOperation(typeof(ConnectionProfileTask), RecordOperation.QueryRecords)]
        public IEnumerable<ConnectionProfileTask> QueryConnectionProfileTasks(int parentID, string sortField, bool ascending, int page, int pageSize, string filterText)
        {
            return m_dataContext.Table<ConnectionProfileTask>().QueryRecords(sortField, ascending, page, pageSize, new RecordRestriction
            {
                FilterExpression = "ConnectionProfileID = {0}",
                Parameters = new object[] { parentID }
            });
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(ConnectionProfileTask), RecordOperation.DeleteRecord)]
        public void DeleteConnectionProfileTask(int id)
        {
            m_dataContext.Table<ConnectionProfileTask>().DeleteRecord(id);
        }

        [RecordOperation(typeof(ConnectionProfileTask), RecordOperation.CreateNewRecord)]
        public ConnectionProfileTask NewConnectionProfileTask()
        {
            return new ConnectionProfileTask();
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(ConnectionProfileTask), RecordOperation.AddNewRecord)]
        public void AddNewConnectionProfileTask(ConnectionProfileTask connectionProfileTask)
        {
            connectionProfileTask.CreatedBy = GetCurrentUserID();
            connectionProfileTask.CreatedOn = DateTime.UtcNow;
            connectionProfileTask.UpdatedBy = connectionProfileTask.CreatedBy;
            connectionProfileTask.UpdatedOn = connectionProfileTask.CreatedOn;

            m_dataContext.Table<ConnectionProfileTask>().AddNewRecord(connectionProfileTask);
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(ConnectionProfileTask), RecordOperation.UpdateRecord)]
        public void UpdateConnectionProfileTask(ConnectionProfileTask connectionProfileTask)
        {
            connectionProfileTask.UpdatedBy = connectionProfileTask.CreatedBy;
            connectionProfileTask.UpdatedOn = connectionProfileTask.CreatedOn;

            m_dataContext.Table<ConnectionProfileTask>().UpdateRecord(connectionProfileTask);
        }

        #endregion

        #region [ Company Table Operations ]

        [RecordOperation(typeof(Company), RecordOperation.QueryRecordCount)]
        public int QueryCompanyCount(string filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
                return m_dataContext.Table<Company>().QueryRecordCount();

            return m_dataContext.Table<Company>().QueryRecordCount(new RecordRestriction("Acronym LIKE {0} OR Name LIKE {0}", $"%{filterText}%"));
        }

        [RecordOperation(typeof(Company), RecordOperation.QueryRecords)]
        public IEnumerable<Company> QueryCompanies(string sortField, bool ascending, int page, int pageSize, string filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
                return m_dataContext.Table<Company>().QueryRecords(sortField, ascending, page, pageSize);

            return m_dataContext.Table<Company>().QueryRecords(sortField, ascending, page, pageSize, new RecordRestriction("Acronym LIKE {0} OR Name LIKE {0}", $"%{filterText}%"));
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(Company), RecordOperation.DeleteRecord)]
        public void DeleteCompany(int id)
        {
            m_dataContext.Table<Company>().DeleteRecord(id);
        }

        [RecordOperation(typeof(Company), RecordOperation.CreateNewRecord)]
        public Company NewCompany()
        {
            return new Company();
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(Company), RecordOperation.AddNewRecord)]
        public void AddNewCompany(Company company)
        {
            company.CreatedBy = GetCurrentUserID();
            company.CreatedOn = DateTime.UtcNow;
            company.UpdatedBy = company.CreatedBy;
            company.UpdatedOn = company.CreatedOn;

            m_dataContext.Table<Company>().AddNewRecord(company);
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(Company), RecordOperation.UpdateRecord)]
        public void UpdateCompany(Company company)
        {
            company.UpdatedBy = company.CreatedBy;
            company.UpdatedOn = company.CreatedOn;

            m_dataContext.Table<Company>().UpdateRecord(company);
        }

        #endregion

        #region [ Vendor Table Operations ]

        [RecordOperation(typeof(Vendor), RecordOperation.QueryRecordCount)]
        public int QueryVendorCount(string filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
                return m_dataContext.Table<Vendor>().QueryRecordCount();

            return m_dataContext.Table<Vendor>().QueryRecordCount(new RecordRestriction("Acronym LIKE {0} OR Name LIKE {0}", $"%{filterText}%"));
        }

        [RecordOperation(typeof(Vendor), RecordOperation.QueryRecords)]
        public IEnumerable<Vendor> QueryVendors(string sortField, bool ascending, int page, int pageSize, string filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
                return m_dataContext.Table<Vendor>().QueryRecords(sortField, ascending, page, pageSize);

            return m_dataContext.Table<Vendor>().QueryRecords(sortField, ascending, page, pageSize, new RecordRestriction("Acronym LIKE {0} OR Name LIKE {0}", $"%{filterText}%"));
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(Vendor), RecordOperation.DeleteRecord)]
        public void DeleteVendor(int id)
        {
            m_dataContext.Table<Vendor>().DeleteRecord(id);
        }

        [RecordOperation(typeof(Vendor), RecordOperation.CreateNewRecord)]
        public Vendor NewVendor()
        {
            return new Vendor();
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(Vendor), RecordOperation.AddNewRecord)]
        public void AddNewVendor(Vendor vendor)
        {
            vendor.CreatedBy = GetCurrentUserID();
            vendor.CreatedOn = DateTime.UtcNow;
            vendor.UpdatedBy = vendor.CreatedBy;
            vendor.UpdatedOn = vendor.CreatedOn;

            m_dataContext.Table<Vendor>().AddNewRecord(vendor);
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(Vendor), RecordOperation.UpdateRecord)]
        public void UpdateVendor(Vendor vendor)
        {
            vendor.UpdatedBy = vendor.CreatedBy;
            vendor.UpdatedOn = vendor.CreatedOn;

            m_dataContext.Table<Vendor>().UpdateRecord(vendor);
        }

        #endregion

        #region [ VendorDevice Table Operations ]

        [RecordOperation(typeof(VendorDevice), RecordOperation.QueryRecordCount)]
        public int QueryVendorDeviceCount(string filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
                return m_dataContext.Table<VendorDevice>().QueryRecordCount();

            return m_dataContext.Table<VendorDevice>().QueryRecordCount(new RecordRestriction("Name LIKE {0}", $"%{filterText}%"));
        }

        [RecordOperation(typeof(VendorDevice), RecordOperation.QueryRecords)]
        public IEnumerable<VendorDevice> QueryVendorDevices(string sortField, bool ascending, int page, int pageSize, string filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
                return m_dataContext.Table<VendorDevice>().QueryRecords(sortField, ascending, page, pageSize);

            return m_dataContext.Table<VendorDevice>().QueryRecords(sortField, ascending, page, pageSize, new RecordRestriction("Name LIKE {0}", $"%{filterText}%"));
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(VendorDevice), RecordOperation.DeleteRecord)]
        public void DeleteVendorDevice(int id)
        {
            m_dataContext.Table<VendorDevice>().DeleteRecord(id);
        }

        [RecordOperation(typeof(VendorDevice), RecordOperation.CreateNewRecord)]
        public VendorDevice NewVendorDevice()
        {
            return new VendorDevice();
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(VendorDevice), RecordOperation.AddNewRecord)]
        public void AddNewVendorDevice(VendorDevice vendorDevice)
        {
            vendorDevice.CreatedBy = GetCurrentUserID();
            vendorDevice.CreatedOn = DateTime.UtcNow;
            vendorDevice.UpdatedBy = vendorDevice.CreatedBy;
            vendorDevice.UpdatedOn = vendorDevice.CreatedOn;

            m_dataContext.Table<VendorDevice>().AddNewRecord(vendorDevice);
        }

        [AuthorizeHubRole("Administrator, Editor")]
        [RecordOperation(typeof(VendorDevice), RecordOperation.UpdateRecord)]
        public void UpdateVendorDevice(VendorDevice vendorDevice)
        {
            vendorDevice.UpdatedBy = vendorDevice.CreatedBy;
            vendorDevice.UpdatedOn = vendorDevice.CreatedOn;

            m_dataContext.Table<VendorDevice>().UpdateRecord(vendorDevice);
        }

        #endregion

        #region [ Modbus Functions ]

        public bool ModbusConnect(string connectionString)
        {
            Dictionary<string, string> parameters = connectionString.ParseKeyValuePairs();

            string frameFormat, transport, setting;
            byte unitID;

            if (!parameters.TryGetValue("frameFormat", out frameFormat) || string.IsNullOrWhiteSpace(frameFormat))
                throw new ArgumentException("Connection string is missing \"frameFormat\".");

            if (!parameters.TryGetValue("transport", out transport) || string.IsNullOrWhiteSpace(transport))
                throw new ArgumentException("Connection string is missing \"transport\".");

            if (!parameters.TryGetValue("unitID", out setting) || !byte.TryParse(setting, out unitID))
                throw new ArgumentException("Connection string is missing \"unitID\" or value is invalid.");

            bool useIP = false;
            bool useRTU = false;

            switch (frameFormat.ToUpperInvariant())
            {
                case "RTU":
                    useRTU = true;
                    break;
                case "TCP":
                    useIP = true;
                    break;
            }

            if (useIP)
            {
                int port;

                if (!parameters.TryGetValue("port", out setting) || !int.TryParse(setting, out port))
                    throw new ArgumentException("Connection string is missing \"port\" or value is invalid.");

                if (transport.ToUpperInvariant() == "TCP")
                {
                    string hostName;

                    if (!parameters.TryGetValue("hostName", out hostName) || string.IsNullOrWhiteSpace(hostName))
                        throw new ArgumentException("Connection string is missing \"hostName\".");

                    return ModbusHubClient.ConnectTcpModbusMaster(hostName, port);
                }

                string interfaceIP;

                if (!parameters.TryGetValue("interface", out interfaceIP))
                    interfaceIP = "0.0.0.0";

                return ModbusHubClient.ConnectUdpModbusMaster(interfaceIP, port);
            }

            string portName;
            int baudRate;
            int dataBits;
            Parity parity;
            StopBits stopBits;

            if (!parameters.TryGetValue("portName", out portName) || string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("Connection string is missing \"portName\".");

            if (!parameters.TryGetValue("baudRate", out setting) || !int.TryParse(setting, out baudRate))
                throw new ArgumentException("Connection string is missing \"baudRate\" or value is invalid.");

            if (!parameters.TryGetValue("dataBits", out setting) || !int.TryParse(setting, out dataBits))
                throw new ArgumentException("Connection string is missing \"dataBits\" or value is invalid.");

            if (!parameters.TryGetValue("parity", out setting) || !Enum.TryParse(setting, out parity))
                throw new ArgumentException("Connection string is missing \"parity\" or value is invalid.");

            if (!parameters.TryGetValue("stopBits", out setting) || !Enum.TryParse(setting, out stopBits))
                throw new ArgumentException("Connection string is missing \"stopBits\" or value is invalid.");

            return ModbusHubClient.ConnectSerialModbusMaster(useRTU, portName, baudRate, dataBits, parity, stopBits);
        }

        #endregion

        #region [ Miscellaneous Functions ]

        /// <summary>
        /// Gets current user ID.
        /// </summary>
        /// <returns>Current user ID.</returns>
        public string GetCurrentUserID()
        {
            return Thread.CurrentPrincipal.Identity?.Name ?? UserInfo.CurrentUserID;
        }

        #endregion
    }
}
