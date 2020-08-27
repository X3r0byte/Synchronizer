# Clean Slate

This is a shell app as a boilerplate to a great looking WPF application. I typically create a new repo with this code to begin developing new software. It is much faster than scaffolding out a new project from scratch every time!

IMPORTANT
The folders x64 and x86 found in the root of the project files MUST CONTAIN Sqlite.Interop.dll! Doing this will ensure the Application Files in the build will include the library in the setup manifest. Visual Studio fails to automagically do this for some reason. The project is already set up for this.

## Authors

* **X3r0byte** - *Initial work* - [X3r0byte](https://github.com/X3r0byte)

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details

## Acknowledgments

* MahApps and its contributors for Metro UI!
* Good directions on how to set up a visual query editor for SQLite [here](https://kenfallon.com/adding-sqlite-as-a-datasource-to-sqleo/)
* SQLeo Download [here](https://sourceforge.net/projects/sqleo/)
* SQLite JDBC Driver for SQLeo [here](https://mvnrepository.com/artifact/org.xerial/sqlite-jdbc)
* SyncFusion *these controls are NOT FREE if you revenue over 1M*