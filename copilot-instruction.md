# Copilot Instructions for Banking Application

This file provides context and guidance for GitHub Copilot to ensure code consistency, adherence to design patterns, and security checks.

## 1. Platform Package Usage
*   **Prioritize:** Use platform packages and custom libraries for core functionalities.
    *   `Platform.Security`: Implement for all authentication, authorization, and encryption.
    *   `Platform.Data`: Utilize for data access and validation, adhering to predefined methods.
*   **Avoid:** rewriting duplicate code.

## 2. Coding Standards & Design Patterns
*   **Security First:** Prioritize secure coding practices.
    *   Implement input validation and sanitization across the application.
    *   Ensure proper authentication and authorization checks are in place.
*   **Design Patterns:** Follow established design patterns for maintainability and scalability.

## 3. Security Checks
*   **Flagging:** Add comments where potential security vulnerabilities may exist or require further review by the security team.
*   **Review:** Code generated should be reviewed for compliance with security standards.

