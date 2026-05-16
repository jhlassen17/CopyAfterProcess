# CopyAfterProcess 📁⚙️

Automate file copy or move operations after a process completes—perfect for post-processing workflows like encoding, downloads, or file generation.

🔗 Repo: https://github.com/jhlassen17/CopyAfterProcess

---

## 🚀 Overview

**CopyAfterProcess** is a .NET utility designed to monitor a process and automatically copy or move files once that process finishes.

This is especially useful for workflows where files are generated or modified by external tools (e.g., encoders, renderers, downloaders), and need to be relocated, organized, or processed afterward.

---

## ✨ Features

- ✅ Monitor external process execution
- ✅ Automatically copy or move files after completion
- ✅ Supports configurable source and destination paths
- ✅ Handles post-processing workflows cleanly
- ✅ Lightweight and easy to integrate
- ✅ Ideal for automation pipelines

---

## 🧑‍💻 Use Cases

- 🎬 Move encoded video files after HandBrake completes
- 📥 Organize downloaded files after processing
- 🧪 Post-process output from scripts or tools
- 🗂️ Automatically sort files into structured folders
- 🔄 Chain workflows together without manual steps

---

## ⚙️ How It Works

1. Start or monitor a target process  
2. Wait for the process to complete  
3. Locate the output file(s)  
4. Copy or move them to a destination  
5. Optionally overwrite or skip existing files  

---

## 🔁 Example Workflow

### Scenario: Video Encoding

1. HandBrakeCLI encodes a video
2. Encoding finishes
3. CopyAfterProcess detects completion
4. File is moved to your media library

---

## 🧪 Example (Conceptual Code)

```
var processor = new CopyAfterProcess
{
    SourcePath = @"C:\Encoding\output.mkv",
    DestinationPath = @"D:\Media\Movies\output.mkv",
    MoveFile = true
};

processor.RunAfter(process);
```

---


## 🛠️ Tech Stack
- C#
- .NET
- System.Diagnostics (process monitoring)
- System.IO (file operations)

---

## 📦 Installation

```
git clone https://github.com/jhlassen17/CopyAfterProcess.git
cd CopyAfterProcess
dotnet build
```

---

## ⚠️ Notes

- Ensure the target process fully releases file handles before copying
- Large files may require retry logic depending on timing
- Destination paths must exist or be handled in code

---

## 🔮 Future Enhancements
- Retry logic for locked files
- File pattern matching (wildcards)
- Logging and diagnostics
- Parallel processing support
- Config file (JSON/YAML)
- CLI interface


---

## 🤝 Contributing
Contributions are welcome!

- Open issues for bugs or feature ideas
- Submit pull requests for improvements

---

## 📄 License
This project is licensed under the MIT License.

See the full license in the LICENSE file.

---

## 👨‍💻 Author

**Jeffrey Lassen**  
Version: `1.0.1.2`  
Last Updated: `05/16/2026`

https://github.com/jhlassen17

---

## 📄 License

This project is licensed under the MIT License.

See the full license in the LICENSE file.

---

## ☕ Support

If you find this useful:  
👉 https://buymeacoffee.com/hanf

---
