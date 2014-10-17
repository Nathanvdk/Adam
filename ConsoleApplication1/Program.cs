using System.Collections.Generic;
using System.Configuration;
//using System.Data.SqlClient;
//using Adam.Core.DatabaseManager;
//using Adam.Tools.PowerCollections;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Adam.Core.DatabaseManager;

namespace ConsoleApplication1
{
	class Program
	{
		static void Main(string[] args)
		{
			// Look for the name in the connectionStrings section.
			var settings = ConfigurationManager.ConnectionStrings["Adam"];

			// If found, return the connection string. 
			if (settings != null)
			{
				var sqlConnection = new SqlConnection(settings.ConnectionString);

				ExecuteSqlFile(sqlConnection, @"C:\workspaces\TFSServer\Adam ASF\Development\v5.x\Database\660.sql");
				ExecuteSqlFile(sqlConnection, @"C:\workspaces\TFSServer\Adam ASF\Development\v5.x\Database\662.sql");

				var upgrader663 = new Upgrader663();
				upgrader663.Update(sqlConnection);

				ExecuteSqlFile(sqlConnection, @"C:\workspaces\TFSServer\Adam ASF\Development\v5.x\Database\664.sql");

				var upgrader665 = new Upgrader665();
				upgrader665.Update(sqlConnection);

				var upgrader = new Upgrader();
				upgrader.Update(sqlConnection);
			}
		}

		private static void ExecuteSqlFile(SqlConnection connection, string file)
		{
			string script = File.ReadAllText(file);

			// split script on GO command
			IEnumerable<string> commandStrings = Regex.Split(script, @"^\s*GO\s*$",
									 RegexOptions.Multiline | RegexOptions.IgnoreCase);

			connection.Open();
			foreach (var commandString in commandStrings.Where(commandString => commandString.Trim() != ""))
			{
				using (var command = new SqlCommand(commandString, connection))
				{
					command.ExecuteNonQuery();
				}
			}
			connection.Close();
		}
	}
}
