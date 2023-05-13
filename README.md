# Werkr - An open source task automation and workflow orchestration project.

Introducing the Werkr Task Automation Project - your one-stop shop for task automation and workflow orchestration.

<a href="https://docs.werkr.app"><img src="https://docs.werkr.app/images/WerkrLogoWithText.png" alt="Werkr Logo & Text" width="300" height="360"></a>

Whether you need a simple task scheduler/cron replacement or a comprehensive task orchestration platform, the Werkr
project has got you covered. Revolutionize the way you automate tasks and orchestrate workflows with our user-friendly
platform, designed to meet a wide range of automation needs.  

The Werkr project has two primary components: a Server and an Agent.  
Both the Server and Agent are supported on a diverse set of operating systems and system architectures.
Currently, both Windows 10+ and Debian Linux based platforms (with systemd) are supported on both x64 and
arm64 cpu architectures. MacOS support is also planned for sometime after the .NET 8 release in November 2023.  

<br/>

# Streamlined Task Management:
With the Werkr project, you can predefine tasks to run on a schedule, create ad-hoc tasks to run immediately,
set tasks to run within a specific time frame, along with so many more configurable options. The choice is yours!  

Visit [docs.werkr.app](https://docs.werkr.app) to explore the Werkr Task Automation Project.  

Both server and agent are offered for download in portable and installer form.  
Once installed there is no difference between the two versions.
The installers simply make the initial setup process a breeze.  

<br/>

## Downloads:
- [Server Downloads](https://server.werkr.app/releases/latest)
- [Agent Downloads](https://agent.werkr.app/releases/latest)

For users windows, download the latest msi installer for your cpu architecture (probably x64).  
For users with Debian linux based operating systems (that have systemd enabled), select the latest .deb file
for your cpu architecture.  
When in doubt select the x64 version.  


<br/><br/>


# Documentation and Support:
* [Quick Start Guide](#quick-start-guide)
* [How To Articles](https://docs.werkr.app/articles/HowTo/index.html)
* [API Documentation](https://docs.werkr.app/api/index.html)
* Troubleshooting Guide (coming soon!)
* [Contributors Guide](#contributing)
* FAQ (coming soon!)


<br/><br/>


# Werkr Server/Agent features:

## A Workflow-Centric Design:
The Werkr project primarily operates on a workflow, or directed acyclic graph (DAG), model.  
The workflow model and DAG visualizations allow you to easily create and manage series of interconnected tasks.  

<br/>

## Schedulable Tasks:
Tasks are the fundamental building blocks of your automation workflows.  
Tasks can be scheduled to run inside or outside of a workflow.  
  * Tasks outside a workflow can be scheduled to run at specific times, on pre-defined and cyclical schedules.
  * Tasks ran inside a workflow have additional trigger mechanisms[*](#flexible-task-triggers).

<br/>

## Versatile Task Types:
Choose from five primary task types to build your workflow(s):  

<br>

### System-Defined Tasks:
Perform common operations like file and directory manipulation with ease, thanks to Werkr's prebuilt system tasks.  
Enjoy consistent output parameters and error handling for the following operations:  
  * File and directory creation.
  * Moving and copying files and directories.
  * Deleting files and directories.
  * Determine whether files or directories exist.
  * Write pre-defined and dynamic content to a file.

<br/>

### PowerShell Script Execution:
Run PowerShell scripts effortlessly and receive standard PowerShell outputs.  

<br/>

### PowerShell Command Execution:
Execute PowerShell commands and access standard PowerShell outputs.  

<br/>

### System Shell Command Execution:
Run commands in your operating system's native shell and get the exit code from the command execution.  

<br/>

### User-Defined Tasks:
Customize your workflows by creating your own tasks. Combine system-defined tasks, PowerShell scripts or commands,
and native command executions into your own free-form repeatable task.  
Branch, iterate, and handle exceptions with ease!  

<br/>

## Flexible Task Triggers:
Start your tasks using various triggers, or combinations of triggers, including:  
  * FileWatch
    * Monitor file system events in real-time or by polling a path periodically.
  * DateTime
    * Set a specific time to run your tasks.
  * On an Interval/Cyclically
    * Run tasks periodically.
  * Task Completion States
    * Trigger tasks based on the completion state of other tasks within the same workflow.
  * Workflow Completion State
    * Trigger tasks based on the operating state of an outside workflow.


<br/> <br/>


# Example Use Cases:
* (Placeholder)


<br/><br/>


# Security:
The Werkr project has a wide variety of very powerful tools. So, security is taken quite seriously and there are some
mandatory steps that must be taken to set up the server and agent initially.  

* TLS certificates are mandatory for the webserver and agent.
* The server and agent undergo an API key registration process before tasks can be run on the system.
  * The agent generates an API key on first startup (and upon request thereafter). The generated API key must be
  registered with a server within 12 hours of its generation.
  * The server and agent then perform a mutual registration process using the API key where they record the opposing
  parties' certificate information (ex HSTS information?).

## Additional Security Considerations:
* Access Control
  * The webserver has built-in user roles that make it easy to restrict access to key and sensitive parts of the system.
* Native 2fa support (TOTP) is built in to the webserver.


<br/><br/>


# Licensing and Support:
The Werkr Task Automation Project is offered free of charge, without any warranties, under an
[MIT license](https://docs.werkr.app/LICENSE.html)!  
Unfortunately, it does not come with any form of guaranteed or implied support.  
Best effort support and triage will be offered on a volunteer basis via a
[GitHub issue](https://werkr.App/issues/new/choose) process.  


<br/><br/>


# Quick Start Guide:
* (Placeholder)
* Example 1: ...
* Example 2: ...


<br/><br/>


# Contributing:
The Werkr Task Automation Project is in its early stages and we're excited that you're interested in contributing!  
We believe that open collaboration is key to the project's success and growth.  
We welcome contributions from developers, users, and anyone interested in task automation and workflow orchestration.  

All official project collaboration will occur via
[GitHub issues](https://werkr.App/issues/new/choose) or [discussions](https://werkr.App/discussions).  

The project has been split into multiple different repositories to keep thing more specific and focused,
so when looking for code please be aware of the following repositories.  
* [Werkr.App](https://werkr.App)
  * The primary documentation repository. Also hosts github pages.
* [Werkr.Server](https://server.werkr.app)
  * The webserver and primary UI interface for the project.
* [Werkr.Agent](https://agent.werkr.app)
  * The agent software that performs the requested tasks.
* [Werkr.Common](https://common.werkr.app)
  * A shared library used by both the Werkr Server and Agent.
* [Werkr.Common.Configuration](https://commonconfiguration.werkr.app)
  * A shared configuration library used by both the Werkr Server and Agent. This is also used by the windows installer.
* [Werkr.Installers](https://installers.werkr.app)
  * A shared [Wix](https://wixtoolset.org/) CustomAction library used by both the Werkr Server and Agent.
  This library is used in the Msi install process.

## Feedback, Suggestions, and Feature Requests:
Do you have an idea for a new feature or enhancement? We'd love to hear it!  
As the project is still in its early stages, your feedback and suggestions are invaluable.  
We encourage you to share your thoughts on features, improvements, and potential use cases.  
You can submit your ideas by creating a
[new feature request](https://werkr.App/issues/new?template=feature_request.yaml).
Be sure to provide a clear description of your proposal and its potential benefits.  

## Documentation Improvements:
If you have suggestions for additional documentation, or corrections for existing documentation, then please submit a
[documentation improvement request](https://werkr.App/issues/new?template=improve_documentation.yaml).  

## Bug Reports:
Please report any bugs, performance issues, or security vulnerabilities you encounter while using the Werkr Task
Automation project by opening a
[new bug report](https://werkr.App/issues/new?&template=bug_report.yaml).  
Be sure to include as much information as possible, such as steps to reproduce the issue, any error messages,
your system's configuration, and any additional context you think we should be aware of.  

## Code Contributions:
If you'd like to contribute code directly to the project, please fork the repository, create a new branch, and submit
a pull request with your changes. We encourage you to follow our existing coding style and conventions.  
Make sure to include a detailed description of your changes in the pull request.  

Additionally you will need to agree to the
[Contribution License Agreement](https://werkr.App/issues/new?template=cla_agreement.yml)
before your PR will be merged.  

We appreciate all contributions, big or small, and look forward to building a vibrant and collaborative community
around the Werkr Task Automation Project. Thank you for your support!  
