Werkr Project 1.0 Intended Feature List. This document is aspirational at this time.

## 1. Streamlined Task Management:
- Predefine tasks or create one-off/ad-hoc tasks that run immediately or on a schedule.
  - Allow users to set start dates, maximum running time length, and end times, for non-workflow defined tasks
  - Enable users to create multiple tasks and link them together into a workflow with simple DAG visualizations for more comprehensive task scheduling.
- Create ad-hoc tasks that run immediately or at a prescheduled time or on a time interval.
  - Provides a user interface for creating and executing ad-hoc tasks
- Create a "workflow" user interface that allows the user to create and link many different tasks together.
- Limited webhook integration allows for task/workflow completion notification via popular project management or productivity tools (Slack, Discord, etc).

<br/>

## 2. The product has two primary components/applications: a Server and an Agent
- Supported on Windows 10+ and Debian Linux (with systemd) based platforms
  - There are MSI installers for the windows releases of each app
  - There are .deb installers for the debian linux release of each app.
  - There are portable editions of the application available as well.
    - There is no difference between the portable edition of the app and the installed version.
- Both server and agent applications support x64 and arm64 CPU architectures
- MacOS support is planned after the .NET 8 release in November 2023
  - Dotnet 8 will provide ALPN support to macos based platforms!

<br/>

## 3. Workflow-Centric Design:
- Directed Acyclic Graph (DAG) visualizations
  - Implements an intuitive UI to display DAGs and current workflow states
    - There is a "workflow" view that only shows a single workflow
    - There is a "system" view that shows all workflows and isolated tasks.
  - Enable users to modify workflows via by modifying the DAG visualization.
    - When in workflow editing mode you can click a button to draw links between tasks and other workflows.
    - Once a link has been created you will receive be prompted on how to handle inputs, outputs, and exceptions.
      - Sensible defaults and intuitive options make workflow configuration simple.
- The Workflow model allows for expanded and advanced capabilities
  - The software provides built-in branching logic, iteration, and exception handling based on task/workflow outputs and state.

<br/>

## 4. Schedulable Tasks:
- Run tasks inside or outside of a workflow
  - Implements a UI for creating and scheduling tasks.
  - Tasks outside of a workflow can only be triggered "immediately", based on a schedule, or on a time interval.
    - Meaning that tasks outside a workflow cannot be triggered by filewatch, outside task completion, or workflow completion.

<br/>

## 5. Flexible Task Triggers:
- FileWatch (Poll FileWatch, Filesystem Event FileWatch)
  - Implement a FileWatch component for triggering tasks based on file events
  - Available as a workflow trigger, as well as a trigger for tasks within a workflow.
- DateTime
  - A scheduler component triggers tasks based on specified DateTime
  - Available for all tasks types and workflows.
- Interval/Cyclical (periodicity)
  - A scheduler component triggers tasks based on starting intervals
  - Available for all tasks types and workflows.
- Task Completion States
  - Tasks may be triggered based on the completion state of other tasks within the same worfklow.
  - Available for use by tasks within a workflow.
- Workflow Completion State
  - Tasks and workflows may be triggered based on the operating state of outside workflows.
  - Available for use by tasks within a workflow, as well as initial workflow triggering.

<br/>

## 6. Versatile Task Types:
- System-defined tasks
  - Contains a library of system-defined tasks with required and optional input parameters
    - File/Directory Creation
    - Move/Copy files and/or directories
    - Delete file and/or directories
    - Test file/directory exists
    - Write Content to file
- User-defined tasks
  - Create a UI for building user-defined tasks.
    - User defined tasks are a linear sequence of system-defined tasks, PowerShell scripts, and native command executions
- PowerShell Script Execution Tasks
- PowerShell Command Execution Tasks
- System Shell Command Execution Tasks

<br/>

## 7. Task Outputs:
- Standard PowerShell Outputs
  - PowerShell Scripts and Command Execution Tasks share the same possible outputs:
  - LastSuccess
  - LastExitCode
    - LastExitCode will be set prior to script execution.
    - Initial LastExitCode can be set to any positive number.
  - Terminating Exception information will be returned (if applicable).
  - Output stream content will be returned as an array of objects.
  - Error stream content will be returned as an array of ErrorRecord objects.
  - Debug, Verbose, and Warning stream content (if any) will be returned as arrays of strings.
- System Shell Command Execution Tasks
  - Returns the process exit code after command execution.
- System-defined tasks
  - Returns the task success status as a [boolean?]. Return output will also contain [Exception?] information (if applicable).

<br/>

## 8. Server and Agent Configuration:
- Both the server is a C# Kestrel webservers that can be configured to access an external (Postgres?) or built-in SQLite database
- The Agent is a C# worker process that hosts PowerShell and can create operating-system shells (predfined by OS).
- The server and agent use grpc for inter-process communications.

<br/>

## 9. Security:
- Access Control
  - Implements role-based access control and standard authentication mechanisms.
  - Manage users roles and permissions to restrict or allow access to certain features or data.
- Native 2FA support (TOTP)
- Implements simple TLS certificate configuration for both the webserver and agent components.

<br/>

## 10. Licensing and Support:
- The applications are offered free of charge under an MIT license.
- Best effort support and triage is provided via a GitHub issue process
- Documentation, tutorials, and other resources are available to help users get started and troubleshoot issues

<br/>

## 11. Community Contributions:
- Open collaboration
  - Community membership and collaboration is encouraged! Please feel free to help with documentation, bug fixes, and new features
  - Please help maintain a welcoming and inclusive environment for all contributors.
- The project has been broken out into multiple separate repositories allowing for focused contributions.

<br/>

## 12. Extensibility and Built-in Utility:
- The werkr project is designed to be extensible but with enough built-in utility to minimize the need for most extensions.
  - Contains a comprehensive library of built-in tasks to cover a wide range of use cases
  - User-defined tasks allow for consistent implementation of commonly performed operations.
  - You have the full utility of PowerShell and the built in system shell at your disposal.
- This product is aimed at users with moderate computer knowledge.
  - You shouldn't have to be a computer expert to create expert level workflows.
