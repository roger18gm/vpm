# VisionPaint – Senior Capstone Project Proposal

Project Name
VisionPaint – Job & Crew Management System for Painting Contractors

---

### Team with Contact Information

Roger Galan-Manzano

Software Engineering Student

---

Stakeholders with Contact Information

Primary Stakeholder:
Vision Painting

The stakeholder is a small painting company interested in improving operational workflows related to project management, employee coordination, and job progress tracking.

---

### Project Purpose

VisionPaint is a web-based, mobile-friendly application designed to streamline operations for small to mid-sized painting companies. Many smaller contractors currently rely on fragmented workflows involving spreadsheets, phone calls, text messages, and paper-based tracking systems. Existing field service management software often contains unnecessary complexity or is generalized toward multiple industries such as HVAC, roofing, plumbing, and electrical work.

The purpose of VisionPaint is to provide a lightweight, specialized platform tailored specifically to residential and commercial painting contractors. The application will help businesses manage jobs, assign workers, track labor hours, and document project progress through a centralized system optimized for mobile field usage.

---

### Background

I have previous novice experience developing full-stack web applications using JavaScript, Node.js, Express, MongoDB, React, and REST APIs. Past academic and personal projects have included authentication systems, CRUD APIs, database schema design, cloud deployment, and automated testing.

Prior projects include:

- A portfolio management API using Node.js, Express, MongoDB, and Mongoose
- Authentication systems using Passport.js and OAuth
- CRUD APIs with automated testing using Jest and Supertest
- Cloud deployment using services such as AWS and Render
  Prior Knowledge

I am recreating something that has been done before but for a more specific use case. There are a few field agent management platforms that exist although these generalize features, mine will be specifically targeting the needs of a small business.

I have not worked in this industry before so I don’t know what I will be getting into in terms of workflows and known solutions.

I am passionate about full stack development and am confident that this type of solution could easily be prototyped in a web-based application.

---

### Description

Overview

VisionPaint is a full-stack web application intended to improve operational efficiency for painting contractors. The application will provide tools for managing jobs, tracking employees, and documenting project progress in a centralized system.

The platform will be optimized for mobile-first usage to support workers and managers operating directly from job sites.

---

## Core Features

### Job & Project Management

- Create and manage painting jobs
- Track project status (Scheduled, In Progress, Completed)
- Store project notes and details
- Assign workers to jobs
  Employee Time Tracking
- Employee clock in/out functionality
- Time tracking tied directly to projects
- Historical time log records
- Break tracking support
  Administrative Dashboard
- Overview of active and completed projects
- Visibility into worker assignments
- Summary of labor hours per project
- Monitoring of overdue or delayed jobs
  Before/After Photo Timeline
- Upload project photos
- Organize photos chronologically
- Visual documentation of project progress
- Job-specific image galleries

---

### Painting-Specific Differentiation

Existing field service management platforms such as Workiz and Fieldy support many industries but are not specifically tailored toward painting workflows. VisionPaint differs by focusing exclusively on painting contractors and emphasizing:

- Simplicity and usability
- Fast mobile interactions
- Painting-specific operational workflows
- Lightweight interfaces for smaller teams
  Potential stretch-goal features include:
- Surface preparation checklists
- Room-by-room progress tracking
- Client project request forms

---

### Intended Audience

The primary users of VisionPaint are:

- Small to mid-sized painting contractors
- Business owners managing multiple job sites
- Field workers requiring mobile-friendly workflow tools
  The application is intended primarily for local residential and commercial painting companies and will initially target smaller teams that may not benefit from larger enterprise software platforms.

---

### Definition of Completion / MVP

VisionPaint will be considered complete when the following workflows are fully functional and deployable:

- User authentication with role-based access
- Job creation and management
- Employee assignment to jobs
- Employee clock in/out functionality
- Administrative dashboard
- Before/after photo upload and viewing
- Mobile-responsive user interface
- Cloud deployment with CI/CD pipelines

Additional features such as room-by-room tracking, advanced checklists, and client-facing functionality may be implemented if time permits.

---

### Significance

This project is significant because it demonstrates the development of a real-world software solution for an actual business workflow problem. The project involves full-stack software engineering, cloud deployment, authentication systems, relational database modeling, responsive UI development, and stakeholder collaboration.
The project is highly portfolio-worthy and demonstrates practical engineering skills that are relevant to industry software development positions. Skills highlighted on a resume could include:

- Full-stack web application development
- ASP.NET Core API development
- PostgreSQL database design
- Vue frontend engineering
- Cloud deployment and CI/CD pipelines
- Session-based authentication systems
- Responsive mobile-first design
- Stakeholder-driven software development

---

### New Computer Science / Software Engineering Concepts

This project will require learning and applying several new concepts beyond previous coursework.
Session-Based Authentication
Implementing secure cookie-based authentication and role-based authorization using ASP.NET Core.
Relational Database Modeling
Designing normalized PostgreSQL schemas representing users, jobs, worker assignments, time logs, and media assets.

Cloud Infrastructure Integration
Deploying and integrating multiple cloud services including:

- Azure App Services
- Firebase Hosting
- Supabase PostgreSQL
- Supabase Storage

CI/CD Pipelines

Implementing automated build and deployment workflows connected to source control pipelines.

File Storage Architecture

Handling image upload workflows and organizing cloud-hosted project photos.

Mobile-First Responsive Design

Designing interfaces optimized for mobile field usage across varying screen sizes.
Stakeholder-Driven Development
Gathering requirements from stakeholders and adapting software design decisions to real-world business workflows.

---

### Interestingness

This project is personally interesting because it combines software engineering with real-world operational workflows and stakeholder collaboration. I have an actual connection with the individuals that would be using this application and am motivated to make it useful. Rather than building a generic application, VisionPaint targets a specific niche with unique workflow requirements.

---

### Milestones, Tasks, and Schedule

The VisionPaint senior project will span two semesters across CSE 499A and CSE 499B. The estimated total effort for the project is approximately 135–145 hours, exceeding the minimum required 126 hours for the senior project sequence.

Current completed effort: ~17 hours

CSE 499A – Planning, Research, Architecture, and Prototype Development

Course Hours Target: ~84 Hours

The primary focus of CSE 499A will be:

- stakeholder research & requirements gathering
- architecture and database design
- authentication systems
- UI/UX planning
- initial feature prototyping
- technology validation

  | Week    | Milestone or Task                                                                                      | Estimated Hours |
  | ------- | ------------------------------------------------------------------------------------------------------ | --------------- |
  | Week 1  | Initial project proposal and stakeholder outreach                                                      | 3               |
  | Week 2  | Frontend/backend integration setup and CI/CD base pipelines                                            | 2               |
  | Week 3  | Database schema architecture, authentication and authorization system implementation                   | 12              |
  | Week 4  | UI/UX wireframes and responsive mobile layout planning, work on frontend app structure if time permits | 7               |
  | Week 4  | UI/UX Design Completion Milestone                                                                      | -               |
  | Week 5  | Stakeholder meetings and workflow verification, test coverage plan and testing pipelines               | 6               |
  | Week 6  | Enhance backend data models, API design, and flesh outs routes                                         | 8               |
  | Week 7  | Initial job management prototype implementation                                                        | 10              |
  | Week 8  | Technology Prototype Demo Milestone                                                                    | —               |
  | Week 8  | Technology Prototype Demo preparation and revisions                                                    | 6               |
  | Week 9  | Employee assignment and time tracking prototype                                                        | 8               |
  | Week 10 | Initial dashboard implementation                                                                       | 6               |
  | Week 11 | Photo upload/storage architecture integration research and implementation                              | 8               |
  | Week 12 | Requirements Specification Submission Milestone                                                        | —               |
  | Week 12 | Requirements specification refinement and documentation                                                | 5               |
  | Week 13 | Prototype polish, work on backlog and other bug fixes                                                  | 5               |
  | Week 14 | Semester review, backlog refinement, and implementation planning for 499B                              | 4               |

Estimated CSE 499A Hours:
~90 Hours

---

CSE 499B – Full Implementation, Testing, and Final Delivery
Course Hours Target: ~42 Hours
The primary focus of CSE 499B will be:

- completing core workflows
- refining user experience
- testing and debugging
- stakeholder validation
- deployment and presentation preparation
  | Week | Milestone / Task | Estimated Hours |
  |-|-|-|
  | Week 1 | Finalize core job management workflows | 5 |
  | Week 2 | Complete employee time tracking workflows | 5 |
  | Week 3 | Administrative dashboard refinement | 4 |
  | Week 4 | Before/after photo timeline completion | 5 |
  | Week 5 | Mobile responsiveness and usability improvements | 4 |
  | Week 6 | User Acceptance Testing (UAT) with stakeholder feedback | 5 |
  | Week 7 | Bug fixing and feature refinement | 4 |
  | Week 8 | Deployment testing and production stability improvements | 3 |
  | Week 9 | Documentation and technical write-up | 3 |
  | Week 10 | Final presentation preparation and demo rehearsals | 2 |
  | Week 11 | Buffer for unexpected issues and refinements | 3 |
  | Week 12–14 | Final Project Demonstration and Submission | — |
  Estimated CSE 499B Hours:
  ~43 Hours

---

### Critical Milestones

CSE 499A

- Initial Proposal Submission
- Technology Prototype Demo (Week 8)
- Requirements Specification Submission (Week 12)
- UI/UX Design Completion
  CSE 499B
- User Acceptance Testing (UAT)
- Final Deployment Stability Review
- Final Senior Project Demonstration

---

### Definition of Success

The project will be considered successful if the following workflows are fully functional and deployable:

- Secure user authentication with role-based access
- Job and project management
- Employee assignment and time tracking
- Administrative dashboard visibility
- Before/after photo uploads and viewing
- Responsive mobile-friendly user experience
- Stable cloud deployment with CI/CD pipelines
  Additional stretch-goal features such as room-by-room tracking and advanced checklist systems may be implemented depending on project progress and available time.

---

### Resources

Software & Services

- Visual Studio / VS Code
- GitHub
- Azure App Services
- Firebase Hosting
- Supabase PostgreSQL
- Supabase Storage
  Frameworks & Libraries
- ASP.NET Core
- Vue
- TypeScript
- Tailwind CSS
- Entity Framework Core
  Learning Resources
- Microsoft ASP.NET documentation
- Supabase documentation
- Vue and Tailwind documentation
- Online tutorials and technical articles
  Estimated Cost
  Most services will remain within free-tier usage limits during development. Estimated cost is expected to stay under $0 for the duration of the project.

---

### Dependencies

The project depends on:

- ASP.NET Core backend services
- Vue frontend application
- PostgreSQL database availability
- Supabase cloud storage
- GitHub repositories and CI/CD pipelines
- Azure and Firebase hosting services
  Development will primarily occur on a Windows development environment using Jet Brains Rider and VS Code.
  Testing will occur locally and through deployed cloud environments.
  The project also depends on continued stakeholder communication for validating workflows and feature priorities.

---

### Risks

Scope Creep

Additional painting-specific features could exceed available development time.

Mobile UX Complexity

Creating an intuitive mobile-first interface may require significant iteration and testing.

File Upload & Storage Complexity

Photo management introduces additional backend, storage, and UI complexity.

Stakeholder Requirement Changes

Stakeholder feedback may alter feature priorities during development.

Time Constraints

Balancing feature implementation, testing, deployment, and documentation within a semester timeline may require reducing scope.
