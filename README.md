# Synchronizer

This app allows you to sync two databases in a client-server configuration using Microsoft Synchronization Framework. It has a simple UI for visualizing what is in each database, as well as a data viewer for the tables.

## Problem
Most all of the deployments and implementations of Microsoft Sync Framework do not allow for creating multiple referenced tables via foreign keys. This makes it difficult to deploy a partially offline database that must be sync'd when set up with traditional auto incremented primary keys, due to a legacy database, or just old design principles.

## Solution
The highlight of this app is it preserves referential integrity and cascade updating keys. For example, the client can create multiple referenced records via foreign keys locally, and upon sync, the server will re assign the auto numbered keys and cascade update them through the client database, preserving referential integrity, and a singular distributed key. The sync framework relies on totally unique GUIDs to track with triggers and tracking tables.

## IMPORTANT

Server tables must have:
* a uniqueidentifier GUID column
* an index on the GUID column for uniqueness
* a seperate unique index on the primary key column
* primary key should be first column (is a best practice anyway)

## Disclaimer(s)

* I haven't had the time to make this app robust enough to "plug and play" right away. In other words, you'll have to familiarize yourself with the code, test it in Visual Studio, and wade through some of the exceptions.
* Much of the code can (and should) be extended upon for robustness. This is more of a GUI/platform implementation app for the Sync Framework, which is not well documented, and does not have support.
* This project was to help me develop other applications that required database sync. I threw it on GitHub in case the knowledge can be reused :)

## Authors

* **X3r0byte** - *Initial work* - [X3r0byte](https://github.com/X3r0byte)

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details

## Acknowledgments

* MahApps and its contributors for Metro UI!
* SyncFusion *these controls are NOT FREE if you revenue over 1M*
