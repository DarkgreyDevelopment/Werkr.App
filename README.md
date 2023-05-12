# Werkr Task Automation Project

Introducing the Werkr Task Automation Project - your one-stop shop for task automation and workflow orchestration.

<img src="https://github.com/DarkgreyDevelopment/Werkr.App/blob/main/docs/images/WerkrLogoWithText.png" alt= "Werkr Logo & Text" width="300" height="360">

Whether you need a simple Task Scheduler/cron replacement or a comprehensive task orchestration platform, the Werkr project has got you covered. Revolutionize the way you automate tasks and orchestrate workflows with our user-friendly platform, designed to meet a wide range of automation needs.

The Werkr project has two primary components: a Server and an Agent. Both the Server and Agent are supported on a diverse set of operating systems and system architectures. Currently, both Windows 10+ and Debian Linux based platforms (with systemd) are supported on both x64 and arm64 cpu architectures. MacOS support will be arriving sometime after the .NET 8 release in November 2023.

<br/>

# Streamlined Task Management:
With the Werkr project, you can predefine tasks to run on a schedule, create ad-hoc tasks to run immediately, set tasks to run within a specific time frame, along with so many more configurable options. The choice is yours!

Visit [Werkr.App](https://werkr.app) to explore the Werkr Task Automation Project.

Both server and agent are offered for download in portable and installer form. Once installed there is no difference between the two versions. The installers simply make the initial setup process a breeze.
* [Server Downloads](https://github.com/DarkgreyDevelopment/Werkr.Server/releases)
* [Agent Downloads](https://github.com/DarkgreyDevelopment/Werkr.Agent/releases)
* For windows, download the latest msi installer for your cpu architecture.
* Debian linux based operating systems (that have systemd enabled), select the latest .deb file for your cpu architecture.
* When in doubt select the x64 version.


<br/><br/>


# Documentation and Support (Placeholder):
* Quick Start Guide
* User Guide
* API Documentation
  * [Feature List](./FeatureList.md)
* Troubleshooting Guide
* Contributors Guide
  * [Development Outline](./Pre-Edit-High-Level-Design-Flow.md)
* FAQ


<br/><br/>


# The Werkr Server/Agent combo features

## A Workflow-Centric Design:
The Werkr project primarily operates on a workflow, or directed acyclic graph (DAG), model.
The workflow model and DAG visualizations allow you to easily create and manage series of interconnected tasks.

<br/>

## Schedulable Tasks:
Tasks are the fundamental building blocks of your automation workflows.
Tasks can be scheduled to run inside or outside of a workflow.
  * Tasks outside a workflow can be scheduled to run at specific times, on pre-defined intervals, and on cyclical schedules.
  * Tasks ran inside a workflow have additional trigger mechanisms[*](#flexible-task-triggers).

<br/>

## Versatile Task Types:
Choose from five primary task types to build your workflow(s):

<br>

### System-Defined Tasks
Perform common operations like file and directory manipulation with ease, thanks to Werkr's prebuilt system tasks.
Enjoy consistent output parameters and error handling for the following operations:
  * File and directory creation.
  * Moving and copying files and directories.
  * Deleting files and directories.
  * Determine whether files or directories exist.
  * Write pre-defined and dynamic content to a file.

<br/>

### PowerShell Script Execution
Run PowerShell scripts effortlessly and receive standard PowerShell outputs.

<br/>

### PowerShell Command Execution
Execute PowerShell commands and access standard PowerShell outputs.

<br/>

### System Shell Command Execution
Run commands in your operating system's native shell and get the exit code from the command execution.

<br/>

### User-Defined Tasks
Customize your workflows by creating your own tasks. Combine system-defined tasks, PowerShell scripts or commands, and native command executions into your own free-form repeatable task.
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


# Example Use Cases (Placeholder):


<br/><br/>


# Getting Started Examples (Placeholder):
* Example 1: ...
* Example 2: ...


<br/><br/>


# Security:
The Werkr project has a wide variety of very powerful tools. So, security is taken quite seriously and there are some mandatory steps that must be taken to set up the server and agent properly.

* Access Control
  * The webserver has built-in user roles that make it easy to restrict access to key and sensitive parts of the system.
* Native 2fa support (TOTP) built in.
* TLS certificates are mandatory for the webserver and agent.
* The server and agent undergo an API key registration process before tasks can be run on the system.
  * The agent generates an API key on first startup (and upon request thereafter). The generated API key must be registered with a server within 12 hours of its generation.
  * The server and agent then perform a mutual registration process using the API key where they record the opposing parties' certificate information (ex HSTS information?).


<br/><br/>


# Licensing and Support:
The Werkr Task Automation Project is offered free of charge under an MIT license!
Unfortunately, it does not come with any form of guaranteed support. Best effort support and triage will be offered via a GitHub issue process.


<br/><br/>


# Contributing
The Werkr Task Automation Project is in its early stages and we're excited to have you on board! We believe that open collaboration is key to the project's success and growth. We welcome contributions from developers, users, and anyone interested in task automation and workflow orchestration.

All official project collaboration will occur via github issues. The project has been split into multiple different repositories to keep thing more specific and focused, so please try to post to the most relevent location!

* [Werkr.App](https://github.com/DarkgreyDevelopment/Werkr.App)
  * The primary documentation repository. Also hosts github pages.
* [Werkr.Server](https://github.com/DarkgreyDevelopment/Werkr.Server)
  * The webserver and primary UI interface for the project.
* [Werkr.Agent](https://github.com/DarkgreyDevelopment/Werkr.Agent)
  * The agent software that performs the requested tasks.
* [Werkr.Common](https://github.com/DarkgreyDevelopment/Werkr.Common)
  * A shared library used by both the Werkr Server and Agent.
* [Werkr.Common.Configuration](https://github.com/DarkgreyDevelopment/Werkr.Common.Configuration)
  * A shared configuration library used by both the Werkr Server and Agent. This is also used by the windows installer.
* [Werkr.Installers](https://github.com/DarkgreyDevelopment/Werkr.Installers)
  * A shared Wix CustomAction library used by both the Werkr Server and Agent. This library is used in the Msi install process.

## Feedback and Suggestions
As the project is still in its early stages, your feedback and suggestions are invaluable. We encourage you to share your thoughts on features, improvements, and potential use cases. You can submit your ideas by creating a new issue on the appropriate GitHub repository site.

## Bug Reports
Please report any bugs or issues you encounter while using the Werkr Task Automation Project by opening a new issue in the appropriate GitHub repository. Be sure to include as much information as possible, such as steps to reproduce the issue, any error messages, and your system's configuration.

## Feature Requests
Do you have an idea for a new feature or enhancement? We'd love to hear it! Please submit your feature request by opening a new issue on the GitHub repository, and be sure to provide a clear description of your proposal and its potential benefits.

## Code Contributions
If you'd like to contribute code directly to the project, please fork the repository, create a new branch, and submit a pull request with your changes. We encourage you to follow our existing coding style and conventions. Also, make sure to include a detailed description of your changes in the pull request.

We appreciate all contributions, big or small, and look forward to building a vibrant and collaborative community around the Werkr Task Automation Project. Thank you for your support!
