# Senior Capstone Project Proposal

**Project Title:** VisionPaint – Job & Crew Management System for Painting Contractors

## 1. Project Overview

VisionPaint is a web-based, mobile-friendly application designed to streamline operations for small to mid-sized painting companies. The system will provide tools for managing jobs, tracking employee work, and improving visibility into project progress.

While existing field service management platforms offer similar capabilities, they are typically generalized across multiple industries (e.g., HVAC, plumbing, roofing). This project aims to deliver a **specialized, lightweight solution tailored specifically to commercial and residential painting businesses**, emphasizing usability, speed, and simplicity.

---

## 2. Problem Statement

Small painting companies often rely on fragmented workflows involving phone calls, text messages, spreadsheets, or manual tracking. Existing software solutions can be overly complex, expensive, or not tailored to the unique needs of painting contractors.

This leads to:

- Inefficient job tracking
- Limited visibility into employee activity
- Poor documentation of project progress
- Increased administrative overhead

---

## 3. Proposed Solution

VisionPaint will provide a centralized platform that enables painting companies to:

- Manage jobs from creation to completion
- Assign employees to job sites
- Track work hours tied directly to specific projects
- Monitor real-time project progress
- Capture and organize visual documentation of work

The application will be optimized for **mobile-first usage**, allowing workers and managers to interact with the system directly from the field.

---

## 4. Key Features

### 4.1 Job & Project Management (Core Feature)

- Create and manage painting jobs (client, location, scope, deadlines)
- Track job status (Scheduled, In Progress, Completed)
- Assign employees to jobs
- Store notes and updates

### 4.2 Employee Time Tracking

- Clock in/out functionality tied to specific job sites
- Track work hours per project
- Basic break logging
- Historical time records

### 4.3 Admin Dashboard

- Overview of active and completed jobs
- Visibility into employee assignments
- Summary of hours worked per job
- Identification of overdue or delayed projects

### 4.4 Painting-Specific Enhancements (Differentiators)

- **Surface Preparation Checklist**
  - Standardized checklist for job readiness (e.g., sanding, priming, masking)

- **Before/After Photo Timeline**
  - Upload and organize images by job and date
  - Visual documentation of progress and completed work

- **Room-by-Room Progress Tracking**
  - Break down projects into smaller units (rooms/areas)
  - Track completion status at a granular level

### 4.5 Client Interaction (Optional Stretch Goal)

- Public-facing form for job requests
- Basic client visibility into project progress

---

## 5. Target Users

- Small to mid-sized painting contractors
- Business owners managing multiple job sites
- Field workers needing simple, mobile-friendly tools

---

## 6. Technical Approach

### 6.1 Architecture

- Full-stack web application
- RESTful API backend
- Client-server architecture

### 6.2 Technology Stack

- **Frontend:** Vite React (mobile-first design) / Tailwind (Google Cloud Firebase hosting)
- **Backend:** C# ASP.NET (Azure Web App/web service hosting)
- **Database:** PostgreSQL (Supabase hosted)
- **Authentication:** JWT and/or OAuth
- **Deployment:** Cloud-based hosting

---

## 7. Key Design Considerations

### 7.1 Mobile-First User Experience

The application will be optimized for use on mobile devices to support workers in the field.

### 7.2 Performance & Responsiveness

Fast interactions and minimal load times will be prioritized to ensure usability during active job work.

### 7.3 Multilingual Accessibility

The system will support multiple languages to accommodate diverse workforces.

### 7.4 Simplicity

The interface will focus on essential features only, avoiding unnecessary complexity common in existing solutions.

---

## 8. Differentiation from Existing Solutions

Unlike generalized field service platforms, VisionPaint will:

- Focus exclusively on painting workflows
- Provide painting-specific tools (checklists, photo tracking, room-level progress)
- Emphasize simplicity and ease of use
- Be tailored for smaller teams rather than large enterprises

---

## 9. Development Plan (High-Level)

- **Phase 1:** Requirements gathering and stakeholder validation
- **Phase 2:** System design (database schema, API structure, UI wireframes)
- **Phase 3:** Core feature implementation (jobs, users, time tracking)
- **Phase 4:** Painting-specific features (checklists, photos, progress tracking)
- **Phase 5:** Testing and refinement
- **Phase 6:** Deployment and final presentation

---

## 10. Expected Outcomes

By the end of the project, VisionPaint will deliver:

- A functional, deployed web application
- A validated solution aligned with stakeholder needs
- Demonstration of full-stack development capabilities
- Practical experience in building a domain-specific software product

---

## 11. Conclusion

VisionPaint aims to bridge the gap between overly complex field service software and the real-world needs of small painting businesses. By focusing on a specific niche and prioritizing usability, this project will demonstrate both technical proficiency and product-oriented thinking.
