using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.Synchronization;
using Microsoft.Synchronization.Data;
using Microsoft.Synchronization.Data.SqlServer;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace CleanSlate
{
	/// Manages connection between server and local databases.
	/// Uses Microsoft Synchronization to sync and de - sync data. Intended to be used as an 'offline/online/offline'
	/// deployment, such as a remote data collection app.
	/// 
	/// [README]
	/// This implementation of Sync Framework is written to sync multiple related tables together while preserving
	/// referential integrity with traditional auto increment PK/FKs. Highlights:
	/// 
	/// LOCAL DATABASE:
	///		* uses faux PK by removing identity insert defined by server, and adding a trigger that auto increments
	///			the column
	///			
	///		* auto increment starts at 1000 to easily troubleshoot syncing, and to avoid duplicate key failures when
	///			syncing to server (this column gets updated with new server PK)
	///		
	///		* the sync happens inside a loop iterating through tables. After letting MS Sync Framework update the server via .Sync():
	///			* requery the server on the table, selecting records that the PK differs between local/server (most likely a new record)
	///			* update the local PK to the newly created server PK
	///			* the CASCADE trigger will detect the update, cascade updating all referenced tables with the new PK
	///			
	///		* if all goes smoothly, the local database will have updated itself with all of the newly created server PKs,
	///			and FK relationships will be in tact
	///			
	///	SERVER DATABASE:
	///		* MUST have a column named "GUID" with constraints: unique; defaultvalue = newid() (generates a new GUID)
	///		
	///		* Probably should have a timestamp column
	///		
	///		* PK ID column (such as EmployeeID, etc) should have a seperate unique constraint
	///		
	///		* MUST have all relationships and constraints defined in server db; this class queries the server and gets fk/indexes
	///			and uses them to work properly
	///		
	/// LOOPING THROUGH TABLES:
	///		tables must be looped in dependancy order. For example, table MyItemType should come before MyItem, if MyItem
	///		has a field named MyItemTypeID (which it should).
	///		
	///		For instance, if the framework creates a new MyItemType record on the server, it will requery MyItemTypeID and send it
	///		back to the local db to update MyItemTypeID. Then, the CASCADE trigger will propogate the updated ID to table MyItem
	///		(because MyItem has reference MyItemTypeID) and preserve the relationship using server calculated IDs.
	///		
	///		If this order were flipped, the new MyItem records would sync before MyItemType, attempting to update the server with an
	///		FK reference that does not exist yet, which will fail and not create the record.
	///		
	public static class Synchronizer
	{
		// connection strings for server/client
		public static string serverConnectionString;
		public static string clientConnectionString;

		// sync orchestrator conducts the sync process
		private static SyncOrchestrator syncOrchestrator;

		// [tablename, pkname] records to sync
		//public static List<string[]> registeredTables;

		// server and client connections
		private static SqlConnection serverConn;
		private static SqlConnection clientConn;

		// server and client provisions
		private static SqlSyncScopeProvisioning serverProvision;
		private static SqlSyncScopeProvisioning clientProvision;

		// misc settings
		private static string remotedb;
		private static string clientdb;
		private static readonly string defaultLocalDB = @"Data Source=(LocalDB)\MSSQLLocalDB;Integrated Security=true;";
		private static readonly string localPKStartingValue = "1000";

		// this creates a prefix on the tracking tables. it helps to
		// start with 'x' so it moves to the bottom of the list in SQL Server Viewer
		private static readonly string trackingPrefix = "xsync";

		private static MainWindow main;

		// set up database, connections, and tables to sync
		static public async Task Init(string clientConnection, string serverConnection)
		{
			main = Application.Current.Windows[0] as MainWindow;
			clientdb = Properties.Settings.Default.LocalDBName;
			remotedb = Properties.Settings.Default.ServerDBName;

			// if the database does not exist
			await Task.Run(() =>
			{
				if (!File.Exists(Properties.Settings.Default.RootDirectory.Replace("/", @"\") + clientdb + @".mdf"))
				{
					try
					{
						// try to create a local database with sqlconnection
						using (var connection = new SqlConnection(defaultLocalDB))
						{
							connection.Open();
							using (SqlCommand command = connection.CreateCommand())
							{
								// create database command with db name and path
								command.CommandText = String.Format("CREATE DATABASE {0} ON PRIMARY (NAME={0}, FILENAME='{1}')",
															clientdb, Properties.Settings.Default.RootDirectory.Replace("/", @"\") + clientdb + @".mdf");
								command.ExecuteNonQuery();

								// detach database so it is not in use
								command.CommandText = String.Format("EXEC sp_detach_db '{0}', 'true'", clientdb);
								command.ExecuteNonQuery();
							}

							clientConnection = connection.ConnectionString;
						}
					}
					catch (Exception ex)
					{
						// fails at making database
						Debug.WriteLine(ex.Message);
					}
				}

				// set the connection strings
				serverConnectionString = serverConnection;
				clientConnectionString = clientConnection;

				// create new orchestrator
				syncOrchestrator = new SyncOrchestrator();

				try
				{
					// setup the connections
					serverConn = new SqlConnection(serverConnectionString);
					clientConn = new SqlConnection(clientConnectionString);

					// if connected, then setup server and client provisions
					if (CheckServerAvailability())
					{
						// setup provisions
						serverProvision = new SqlSyncScopeProvisioning(serverConn);
						clientProvision = new SqlSyncScopeProvisioning(clientConn);

						// assign the tracking prefix
						serverProvision.ObjectPrefix = trackingPrefix;
						clientProvision.ObjectPrefix = trackingPrefix;

						main.context.LocalTables = FetchLocalTables( @"C:\USERS\PUBLIC\LOCAL.MDF");
						main.context.ServerTables = FetchServerTables(remotedb);

						// list of [table, primarykey] to track. The application will automatically
						// create a new local table and sync it to server if it does not exist locally
						// there is a specific order required; please refer to the README
						//registeredTables = new List<string[]>
						//	{
						//		new string[] { "sync_test", "SyncTestID" }
						//		// etc.
						//	};

						// sync data to the local database
						// await SyncAsync();
					}
				}
				catch (Exception ex)
				{
					Debug.WriteLine(ex.Message);
				}
			});
		}

		// awaitable task which syncs the server and local databases
		static public async Task SyncAsync()
		{
			await Task.Run(async () =>
			{
				try
				{
					CreateBackups();
					await Sync();
				}
				catch (Exception ex)
				{
					Debug.WriteLine(ex.Message);
				}
			});
		}

		// awaitable task which syncs the server and local databases
		static public Task DesyncAsync()
		{
			return Task.Run(() =>
			{
				try
				{
					ClearClientTrackingData();
					// ClearTrackingData();

				}
				catch (Exception ex)
				{
					Debug.WriteLine(ex.Message);
				}
			});
		}

		/// <summary>
		/// Synchronizes tracked client and server tables
		/// </summary>
		static public Task Sync()
		{
			return Task.Run(() =>
			{
				// bit to flip if detected new table
				bool dbChanged = false;

				// main loop for all tables in database
				for (int i = 0; i < main.context.LocalTables.Count; i++)
				{
					// string[] registerInfo = main.context.LocalTables[i];

					// get table info
					string tblname = main.context.LocalTables[i].name;
					string id = main.context.LocalTables[i].pkColumn;

					// build description for tables
					DbSyncTableDescription serverTableDesc = SqlSyncDescriptionBuilder.GetDescriptionForTable(tblname, serverConn);
					var clientTableDesc = new DbSyncTableDescription();

					// if the table is not set up in the database, provision the server
					if (!serverProvision.ScopeExists(tblname))
					{
						// db is changed because new table
						dbChanged = true;

						// get the server scope description from table name
						var serverScopeDesc = new DbSyncScopeDescription(tblname);

						// table description settings
						serverTableDesc = SqlSyncDescriptionBuilder.GetDescriptionForTable(tblname, serverConn);
						serverTableDesc.GlobalName = tblname;
						serverTableDesc.LocalName = tblname;
						serverScopeDesc.Tables.Add(serverTableDesc);

						// remove the PK as we do not need to track it (causes issues)
						serverScopeDesc.Tables[tblname].Columns.Remove(serverScopeDesc.Tables[tblname].Columns[id]);

						// trick the framework so it thinks the GUID is PK
						try
						{
							serverScopeDesc.Tables[tblname].Columns["GUID"].IsPrimaryKey = true;
						}
						catch (Exception ex)
						{
							Debug.WriteLine(tblname + " does not have a GUID column. Please add a not null GUID column with newid() for default value.");
							Debug.WriteLine(ex.Message);
						}

						// provision the server
						serverProvision.PopulateFromScopeDescription(serverScopeDesc);
						serverProvision.ObjectPrefix = trackingPrefix;
						serverProvision.Apply();
					}

					// if the table is not set up in the database, provision the client
					if (!clientProvision.ScopeExists(tblname))
					{
						// db is changed because new table
						dbChanged = true;

						var clientScopeDesc = new DbSyncScopeDescription(tblname);

						// try to find the table locally. if exception, catch it
						// and create the table from the server definition with CreateLocalFromServer()
						try
						{
							// table exists, then
							clientTableDesc = SqlSyncDescriptionBuilder.GetDescriptionForTable(tblname, clientConn);
						}
						catch (Exception)
						{
							// table does not exist, then
							CreateLocalFromServer(serverConnectionString, clientConnectionString, tblname, id);
							clientTableDesc = SqlSyncDescriptionBuilder.GetDescriptionForTable(tblname, clientConn);
						}

						// settings for client table description
						clientTableDesc.GlobalName = tblname;
						clientTableDesc.LocalName = tblname;
						clientScopeDesc.Tables.Add(clientTableDesc);
						clientScopeDesc.Tables[tblname].Columns.Remove(clientScopeDesc.Tables[tblname].Columns[id]);

						// provision the client
						clientProvision.PopulateFromScopeDescription(clientScopeDesc);
						clientProvision.ObjectPrefix = trackingPrefix;
						clientProvision.Apply();
					}

					// SyncTable(tblname, id);
					// start the synchronize process, setup providers
					var localProvider = new SqlSyncProvider(tblname, clientConn);
					var remoteProvider = new SqlSyncProvider(tblname, serverConn);

					// set the prefix for the tracking tables
					localProvider.ObjectPrefix = trackingPrefix;
					remoteProvider.ObjectPrefix = trackingPrefix;

					try
					{
						// attach client and server to sync orchestrator
						syncOrchestrator.LocalProvider = localProvider;
						syncOrchestrator.RemoteProvider = remoteProvider;

						// set the direction of sync session. Upload/download favors server changes over client(?)
						syncOrchestrator.Direction = SyncDirectionOrder.UploadAndDownload;

						// execute the synchronization process
						SyncOperationStatistics syncStats = syncOrchestrator.Synchronize();

						// print syncing stats
						Debug.WriteLine("Sync table: " + tblname);
						Debug.WriteLine("Start Time: " + syncStats.SyncStartTime);
						Debug.WriteLine("Changes sent to server: " + syncStats.UploadChangesTotal);
						Debug.WriteLine("Changes downloaded from server: " + syncStats.DownloadChangesTotal);
						Debug.WriteLine("Download changes failed: " + syncStats.DownloadChangesFailed);
						Debug.WriteLine("Complete Time: " + syncStats.SyncEndTime);

						// below is a routine that will update local PKs with server generated
						// PKs if they are different after sync'd.
						// this is VERY important if adding/editing multiple related tables, as the PK update
						// will trigger a cascade to all foreign keys, keeping the relationships in tact
						// and sync'd up properly with the server.

						// this could probably be rewritten, pretty crude at the moment.
						//------------------------------------------------------
						// query back to the server to get the autogenerated PKs
						using (var connection = new SqlConnection(serverConnectionString))
						{
							var cmdServerPK = new SqlCommand("SELECT " + id + ", GUID FROM " + tblname, connection);
							cmdServerPK.Connection.Open();
							SqlDataReader serverPKs = cmdServerPK.ExecuteReader();
							var dtServerPK = new DataTable();

							// load the PK, GUID into a datatable
							dtServerPK.Load(serverPKs);

							// foreach row in PK, GUID
							foreach (DataRow row in dtServerPK.Rows)
							{
								// open a connection to local database
								using (var clientconnection = new SqlConnection(clientConnectionString))
								{
									// query where GUID and PK are equal to server GUID and PK
									string sql = "SELECT " + id + " FROM " + tblname + " WHERE GUID = '" + row["GUID"] + "' AND " + id + " = '" + row[id] + "'";
									var clientpk = new SqlCommand(sql, clientconnection);
									clientpk.Connection.Open();
									SqlDataReader clientPKs = clientpk.ExecuteReader();

									// load the PK, GUID into a datatable
									var clientdt = new DataTable();
									clientdt.Load(clientPKs);
									clientpk.Connection.Close();

									// if there are no rows, that means the PK in client is different than server
									if (clientdt.Rows.Count == 0)
									{
										// update the local PK to server PK where GUID is the same
										// again, this will trigger a cascade on FKs in the client database,
										// so when the table referencing it is sync'd, it will be congruent with 
										// both client and server db.
										var updatepk = new SqlCommand("UPDATE " + tblname +
																	 " SET " + id + " = " + row[id] +
																	 " WHERE GUID = '" + row["GUID"] + "'", clientconnection);
										updatepk.Connection.Open();
										updatepk.ExecuteScalar();
										updatepk.Connection.Close();
									}
								}
							}
						}
					}
					catch (Exception ex)
					{
						Debug.WriteLine(ex.Message);
					}
				}

				// recreate all database constraints after building tables if the database has changed
				if (dbChanged)
				{
					CreateConstraints();
				}
			});
		}

		public static void SyncTable(string tblname, string id)
		{
			// start the synchronize process, setup providers
			var localProvider = new SqlSyncProvider(tblname, clientConn);
			var remoteProvider = new SqlSyncProvider(tblname, serverConn);

			// set the prefix for the tracking tables
			localProvider.ObjectPrefix = trackingPrefix;
			remoteProvider.ObjectPrefix = trackingPrefix;

			try
			{
				// attach client and server to sync orchestrator
				syncOrchestrator.LocalProvider = localProvider;
				syncOrchestrator.RemoteProvider = remoteProvider;

				// set the direction of sync session. Upload/download favors server changes over client(?)
				syncOrchestrator.Direction = SyncDirectionOrder.UploadAndDownload;

				// execute the synchronization process
				SyncOperationStatistics syncStats = syncOrchestrator.Synchronize();

				// print syncing stats
				Debug.WriteLine("Sync table: " + tblname);
				Debug.WriteLine("Start Time: " + syncStats.SyncStartTime);
				Debug.WriteLine("Changes sent to server: " + syncStats.UploadChangesTotal);
				Debug.WriteLine("Changes downloaded from server: " + syncStats.DownloadChangesTotal);
				Debug.WriteLine("Download changes failed: " + syncStats.DownloadChangesFailed);
				Debug.WriteLine("Complete Time: " + syncStats.SyncEndTime);

				// below is a routine that will update local PKs with server generated
				// PKs if they are different after sync'd.
				// this is VERY important if adding/editing multiple related tables, as the PK update
				// will trigger a cascade to all foreign keys, keeping the relationships in tact
				// and sync'd up properly with the server.

				// this could probably be rewritten, pretty crude at the moment.
				//------------------------------------------------------
				// query back to the server to get the autogenerated PKs
				using (var connection = new SqlConnection(serverConnectionString))
				{
					var cmdServerPK = new SqlCommand("SELECT " + id + ", GUID FROM " + tblname, connection);
					cmdServerPK.Connection.Open();
					SqlDataReader serverPKs = cmdServerPK.ExecuteReader();
					var dtServerPK = new DataTable();

					// load the PK, GUID into a datatable
					dtServerPK.Load(serverPKs);

					// foreach row in PK, GUID
					foreach (DataRow row in dtServerPK.Rows)
					{
						// open a connection to local database
						using (var clientconnection = new SqlConnection(clientConnectionString))
						{
							// query where GUID and PK are equal to server GUID and PK
							string sql = "SELECT " + id + " FROM " + tblname + " WHERE GUID = '" + row["GUID"] + "' AND " + id + " = '" + row[id] + "'";
							var clientpk = new SqlCommand(sql, clientconnection);
							clientpk.Connection.Open();
							SqlDataReader clientPKs = clientpk.ExecuteReader();

							// load the PK, GUID into a datatable
							var clientdt = new DataTable();
							clientdt.Load(clientPKs);
							clientpk.Connection.Close();

							// if there are no rows, that means the PK in client is different than server
							if (clientdt.Rows.Count == 0)
							{
								// update the local PK to server PK where GUID is the same
								// again, this will trigger a cascade on FKs in the client database,
								// so when the table referencing it is sync'd, it will be congruent with 
								// both client and server db.
								var updatepk = new SqlCommand("UPDATE " + tblname +
															 " SET " + id + " = " + row[id] +
															 " WHERE GUID = '" + row["GUID"] + "'", clientconnection);
								updatepk.Connection.Open();
								updatepk.ExecuteScalar();
								updatepk.Connection.Close();
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.Message);
			}
		}

		public static ObservableCollection<SyncTable> FetchLocalTables(string dbName)
		{
			try
			{
				var sqlConn = new Microsoft.Data.SqlClient.SqlConnection(clientConnectionString);
				var serverConn = new ServerConnection(sqlConn);
				var server = new Server(serverConn);
				var tables = new ObservableCollection<SyncTable>();

				// get the remote database by name
				Database database = server.Databases[dbName];

				foreach (Table table in database.Tables)
				{

					// if the table is not a tracking table (we don't want to sync the tracking data)
					if (!table.Name.Contains(trackingPrefix))
					{
						var syncTable = new SyncTable { name = table.Name, pkColumn = table.Columns[table.Columns.Count - 1].Name };
						tables.Add(syncTable);
					}
				}

				return tables;
			}
			catch (Exception ex)
			{
				return null;
			}
		}

		public static ObservableCollection<SyncTable> FetchServerTables(string dbName)
		{
			try
			{
				var sqlConn = new Microsoft.Data.SqlClient.SqlConnection(serverConnectionString);
				var serverConn = new ServerConnection(sqlConn);
				var server = new Server(serverConn);
				var tables = new ObservableCollection<SyncTable>();

				// get the remote database by name
				Database database = server.Databases[dbName];

				foreach (Table table in database.Tables)
				{

					// if the table is not a tracking table (we don't want to sync the tracking data)
					if (!table.Name.Contains(trackingPrefix))
					{
						var syncTable = new SyncTable { name = table.Name, pkColumn = table.Columns[0].Name };
						tables.Add(syncTable);
					}
				}

				return tables;
			}
			catch (Exception ex)
			{
				return null;
			}
		}




		/// <summary>
		/// Creates cascades updates fks, and any indexes defined in the server
		/// </summary>
		public static void CreateConstraints()
		{
			// loop created tables in client and edit properties after sync
			for (int i = 0; i < main.context.LocalTables.Count; i++)
			{
				// string[] registerInfo = registeredTables[i];

				// get table info and init connections
				string table = main.context.LocalTables[i].name;
				string id = main.context.LocalTables[i].pkColumn;

				var serverConn = new Microsoft.Data.SqlClient.SqlConnection(serverConnectionString);
				var conn = new ServerConnection(serverConn);
				var remote = new Server(conn);

				// fkschema is a string of sql statements to create fk constraints
				// ixschema is a string of sql statements to create various indexes defined in DB
				string fkschema = "";
				string ixschema = "";

				// get the remote database by name
				Database remoteDB = remote.Databases[remotedb];

				// get the table to copy into local
				Table tbl = remoteDB.Tables[table];

				// loop through and build foreign key scripts
				foreach (ForeignKey key in tbl.ForeignKeys)
				{
					var list = (System.Collections.IList)key.Script();
					for (int i1 = 0; i1 < list.Count; i1++)
					{
						string str = (string)list[i1];
						fkschema += str + " ";
					}

					try
					{
						// get the tablename that the FK references
						string referencedTable = key.ReferencedTable;

						// add the cascade trigger to the client database. This lets the app cascade foreign
						// keys if changed by server to keep references in tact and sync'd with server
						using (var connection = new SqlConnection(clientConnectionString))
						{
							string cascade = " ALTER TABLE " + table +
											 " ADD CONSTRAINT [cascade_" + table + "_" + key.Columns[0].Name + "] " +
											 " FOREIGN KEY(" + key.Columns[0] + ") " +
											 " REFERENCES " + referencedTable + "(" + key.Columns[0] + ") " +
											 " ON UPDATE CASCADE ";
							var cmdCascade = new SqlCommand(cascade, connection);
							cmdCascade.Connection.Open();
							cmdCascade.ExecuteNonQuery();
							cmdCascade.Connection.Close();
						}
					}
					catch (Exception ex)
					{
						Debug.WriteLine(ex.Message);
					}
				}

				// loop through and build index scripts
				foreach (Index ix in tbl.Indexes)
				{
					var list = (System.Collections.IList)ix.Script();
					for (int i1 = 0; i1 < list.Count; i1++)
					{
						string str = (string)list[i1];
						ixschema += str + " ";
					}
				}

				// close connection to server
				serverConn.Close();

				try
				{
					using (var connection = new SqlConnection(clientConnectionString))
					{
						// add the foreign keys to the tables
						if (ixschema != "")
						{
							Debug.WriteLine("***CREATING INDEXES*** " + ixschema);
							var updateix = new SqlCommand(ixschema, connection);
							updateix.Connection.Open();
							updateix.ExecuteNonQuery();
							updateix.Connection.Close();
							Debug.WriteLine("***FINISHED*** ");
						}
					}
				}
				catch (Exception ex) { Debug.WriteLine(ex.Message); }

				try
				{
					using (var connection = new SqlConnection(clientConnectionString))
					{
						// add the foreign keys to the tables
						if (fkschema != "")
						{
							Debug.WriteLine("***CREATING FOREIGN KEYS*** " + fkschema);
							var updatefk = new SqlCommand(fkschema, connection);
							updatefk.Connection.Open();
							updatefk.ExecuteNonQuery();
							updatefk.Connection.Close();
							Debug.WriteLine("***FINISHED*** ");
						}
					}
				}
				catch (Exception ex) { Debug.WriteLine(ex.Message); }

				try
				{
					// (this is optional) drop the delete trigger; may cause unwanted data deletion otherwise
					using (var connection = new SqlConnection(clientConnectionString))
					{
						string dropDeleteTrigger = "DROP TRIGGER [" + trackingPrefix + "_" + table + "_delete_trigger] ";

						Debug.WriteLine("***DROPPING DELETE TRIGGER*** " + dropDeleteTrigger);
						var dropdel = new SqlCommand(dropDeleteTrigger, connection);
						dropdel.Connection.Open();
						dropdel.ExecuteNonQuery();
						dropdel.Connection.Close();
						Debug.WriteLine("***FINISHED*** ");
					}
				}
				catch (Exception ex) { Debug.WriteLine(ex.Message); }

				try
				{
					// drop server delete trigger
					using (var connection = new SqlConnection(serverConnectionString))
					{
						string dropDeleteTrigger = "DROP TRIGGER [" + trackingPrefix + "_" + table + "_delete_trigger] ";

						Debug.WriteLine("***DROPPING DELETE TRIGGER*** " + dropDeleteTrigger);
						var dropdel = new SqlCommand(dropDeleteTrigger, connection);
						dropdel.Connection.Open();
						dropdel.ExecuteNonQuery();
						dropdel.Connection.Close();
						Debug.WriteLine("***FINISHED*** ");
					}
				}
				catch (Exception ex) { Debug.WriteLine(ex.Message); }
			}
		}

		/// <summary>
		/// removes tracking data and tables created by Microsoft Synchronization
		/// </summary>
		public static void ClearTrackingData()
		{
			// create deprovisions
			var serverDeprovisioning = new SqlSyncScopeDeprovisioning(serverConn);
			var clientDeprovisioning = new SqlSyncScopeDeprovisioning(clientConn);

			// tell the deprovision what prefix is being used
			serverDeprovisioning.ObjectPrefix = trackingPrefix;
			clientDeprovisioning.ObjectPrefix = trackingPrefix;

			for (int i = 0; i < main.context.LocalTables.Count; i++)
			{
				// string[] registerInfo = registeredTables[i];
				// try to remove previous data
				string scope = main.context.LocalTables[i].name;
				serverDeprovisioning.DeprovisionScope(scope);
			}

			// deprovision client and server
			serverDeprovisioning.DeprovisionStore();
			clientDeprovisioning.DeprovisionStore();

			using (var connection = new SqlConnection(clientConnectionString))
			{
				// loop backwards to avoid FK conflicts
				for (int i = main.context.LocalTables.Count - 1; i >= 0; i--)
				{
					try
					{
						// string[] registerInfo = registeredTables[i];
						//// ensure that the GUID column is NOT NULL as this is used to sync records
						string droptable = "DROP TABLE " + main.context.LocalTables[i].name + ";";
						var drop = new SqlCommand(droptable, connection);
						drop.Connection.Open();
						drop.ExecuteNonQuery();
						drop.Connection.Close();
					}
					catch (Exception ex)
					{
						Debug.WriteLine(ex.Message);
					}
				}
			}
		}

		/// <summary>
		/// removes tracking data and tables created by Microsoft Synchronization
		/// </summary>
		public static void ClearClientTrackingData()
		{
			// create deprovisions
			var clientDeprovisioning = new SqlSyncScopeDeprovisioning(clientConn);

			// tell the deprovision what prefix is being used
			clientDeprovisioning.ObjectPrefix = trackingPrefix;

			// deprovision client and server
			clientDeprovisioning.DeprovisionStore();

			using (var connection = new SqlConnection(clientConnectionString))
			{
				// loop backwards to avoid FK conflicts
				for (int i = main.context.LocalTables.Count - 1; i >= 0; i--)
				{
					try
					{
						// string[] registerInfo = registeredTables[i];
						string droptable = "DROP TABLE " + main.context.LocalTables[i].name + ";";
						var drop = new SqlCommand(droptable, connection);
						drop.Connection.Open();
						drop.ExecuteNonQuery();
						drop.Connection.Close();
					}
					catch (Exception ex)
					{
						Debug.WriteLine(ex.Message);
					}
				}
			}
		}


		/// <summary>
		/// removes tracking data and tables created by Microsoft Synchronization
		/// </summary>
		public static void ClearSingleTableData(string tableName)
		{
			// create deprovisions
			var clientDeprovisioning = new SqlSyncScopeDeprovisioning(clientConn);

			// tell the deprovision what prefix is being used
			clientDeprovisioning.ObjectPrefix = trackingPrefix;

			// deprovision client and server
			clientDeprovisioning.DeprovisionStore();

			using (var connection = new SqlConnection(clientConnectionString))
			{
				try
				{
					// string[] registerInfo = registeredTables[i];
					string droptable = $"DROP TABLE {tableName};";
					var drop = new SqlCommand(droptable, connection);
					drop.Connection.Open();
					drop.ExecuteNonQuery();
					drop.Connection.Close();
				}
				catch (Exception ex)
				{
					Debug.WriteLine(ex.Message);
				}
			}
		}

		/// <summary>
		/// Imports schema from server and creates a local table.
		/// Server table MUST include auto generated Guid column and a SEPERATE unique index constraint on PK.
		/// </summary>
		/// <param name="server">server connection string</param>
		/// <param name="client">client connection string</param>
		/// <param name="tblname">name of table to import from server</param>
		public static void CreateLocalFromServer(string server, string client, string tblname, string id)
		{
			var serverConn = new Microsoft.Data.SqlClient.SqlConnection(server);

			var conn = new ServerConnection(serverConn);
			// conn.Connect();

			Server remote = new Server(conn);

			string schema = "";

			// get the remote database by name
			Database remoteDB = remote.Databases[remotedb];

			// get the table to copy into local
			Table table = remoteDB.Tables[tblname];

			// build the table schema
			foreach (string str in table.Script())
				schema += str + " ";

			// close connection to server
			serverConn.Close();

			// Debug.Write(schema);

			try
			{
				using (var connection = new SqlConnection(client))
				{
					// create a new local table with the remote schema
					var create = new SqlCommand(schema, connection);
					create.Connection.Open();
					create.ExecuteNonQuery();
					create.Connection.Close();

					// ensure that the GUID column is NOT NULL as this is used to sync records
					string updatenull = "ALTER TABLE " + tblname + " ALTER COLUMN [GUID] uniqueidentifier NOT NULL;";
					var updateNull = new SqlCommand(updatenull, connection);
					updateNull.Connection.Open();
					updateNull.ExecuteNonQuery();
					updateNull.Connection.Close();

					// update the local table and flag the GUID as primary key
					string updatepk = "ALTER TABLE " + tblname + " " +
									" ADD CONSTRAINT PK_GUID_" + tblname + " PRIMARY KEY([GUID]); ";
					var updatePK = new SqlCommand(updatepk, connection);
					updatePK.Connection.Open();
					updatePK.ExecuteNonQuery();
					updatePK.Connection.Close();

					// set a local constraint to generate a new unique id on record create
					string updatedf = " ALTER TABLE " + tblname + " " +
									" ADD CONSTRAINT DF_GUID_" + tblname + " " +
									" DEFAULT newid() FOR[GUID]; ";
					var updateDF = new SqlCommand(updatedf, connection);
					updateDF.Connection.Open();
					updateDF.ExecuteNonQuery();
					updateDF.Connection.Close();


					// below code alters PK column.
					// we will drop and re-add the column, removing the identity insert attribute.
					// then, create a trigger to act as a PK for the app to work properly until
					// sync'd with server again (which reassigns the PK to whatever the server makes)

					// drop pk to remove identity insert
					string updateDropPK = "ALTER TABLE " + table +
						   " DROP COLUMN [" + id + "]";
					var cmdDropPK = new SqlCommand(updateDropPK, connection);
					cmdDropPK.Connection.Open();
					cmdDropPK.ExecuteNonQuery();
					cmdDropPK.Connection.Close();

					// re add pk column
					string updateAddIdentity = "ALTER TABLE " + table +
						" ADD [" + id + "] INT DEFAULT ((0)) NOT NULL";
					var cmdAddIdentity = new SqlCommand(updateAddIdentity, connection);
					cmdAddIdentity.Connection.Open();
					cmdAddIdentity.ExecuteNonQuery();
					cmdAddIdentity.Connection.Close();

					// create an auto incremnt trigger to act as an incremental pk
					// this will be used to auto increment and *locally* track records for reference,
					// it will be overwritten when it is sync'd to the server with whatever the server dictates

					string updatePKTrigger = "CREATE TRIGGER [" + tblname + "_pk_trigger] " +
											" ON " + table +
											" FOR INSERT " +
											" AS " +
											" BEGIN " +
											" UPDATE " + table +
											" SET " + id + " = " + localPKStartingValue + " + (SELECT COUNT(" + id + ") FROM " + table + ") " +
											" WHERE [GUID] in (SELECT [GUID] from inserted) " +
											" END ";
					var cmdPKTrigger = new SqlCommand(updatePKTrigger, connection);
					cmdPKTrigger.Connection.Open();
					cmdPKTrigger.ExecuteNonQuery();
					cmdPKTrigger.Connection.Close();

					//string uniquepk = "CREATE UNIQUE NONCLUSTERED INDEX [UNIQUEIX_" + tblname + "] ON [" + tblname + "]([" + id + "] ASC); ";
					//var cmdUniquePK = new SqlCommand(uniquepk, connection);
					//cmdUniquePK.Connection.Open();
					//cmdUniquePK.ExecuteNonQuery();
					//cmdUniquePK.Connection.Close();
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.Message);
			}
		}

		public static DataTable GetData(string sql, string connectionString)
		{
			try
			{
				var dt = new DataTable();

				using (var con = new SqlConnection(connectionString))
				{
					using (var cmd = new SqlCommand(sql, con))
					{
						var adapter = new SqlDataAdapter(cmd);

						con.Open();
						adapter.Fill(dt);
						con.Close();
					}
				}

				return dt;
			}
			catch (Exception ex)
			{
				throw ex;
			}
		}
		public static bool CheckServerAvailability()
		{
			return true;
			// return Directory.Exists(@"\\yourserverhere");
		}

		public static void CreateBackups()
		{
			// probably a good idea to back data up before sync
		}
	}
}