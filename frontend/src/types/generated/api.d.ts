/**
 * 由后端 Swagger 自动生成 —— 请勿手改。
 * 重新生成：npm run api:types
 * 校验漂移：npm run api:types:check
 */
export interface paths {
    "/api/auth/login": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["LoginRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/auth/sso/providers": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SsoProviderInfo"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/auth/sso/{provider}/authorize": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    mode?: string;
                };
                header?: never;
                path: {
                    provider: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/auth/sso/{provider}/login": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    provider: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SsoLoginRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/auth/sso/{provider}/bind": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    provider: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SsoBindRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/auth/me": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["UserDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/users": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    search?: string;
                    role?: components["schemas"]["UserRole"];
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateUserRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/users/role-counts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/users/options": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    search?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["UserOptionDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/users/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateUserRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/users/{id}/reset-password": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ResetPasswordRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/users/me/password": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ChangeOwnPasswordRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/users/me/sso": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MySsoBindingDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/users/roles": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RoleMatrixDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/nodes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["NodeListResponseDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/nodes/{name}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    name: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/audit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    action?: string;
                    resourceType?: string;
                    username?: string;
                    succeeded?: boolean;
                    from?: string;
                    to?: string;
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AuditLogPageDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/audit/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    action?: string;
                    resourceType?: string;
                    username?: string;
                    succeeded?: boolean;
                    from?: string;
                    to?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/audit/facets": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/recorder/capabilities": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RecorderCapabilitiesDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/recorder/sessions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RecorderSession"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateRecorderSessionRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/recorder/sessions/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RecorderSession"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/recorder/sessions/{id}/steps": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RecorderSnapshot"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/recorder/sessions/{id}/stop": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/recorder/sessions/{id}/save": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SaveRecorderRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/shared-steps": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                    search?: string;
                    page?: string;
                    pageSize?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SharedStepGroupViewPagedResult"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SharedStepGroupRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/shared-steps/options": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SharedStepOption"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/shared-steps/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SharedStepGroupDetail"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SharedStepGroupRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/shared-steps/{id}/usages": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SharedStepUsageDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/shared-steps/batch-delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchDeleteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                    status?: components["schemas"]["TestPlanStatus"];
                    ownerId?: string;
                    releaseName?: string;
                    search?: string;
                    requirementId?: string;
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TestPlanSummaryDtoPagedResult"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateTestPlanRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/summary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/releases": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": string[];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TestPlanDetailDto"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateTestPlanRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/{id}/status": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SetPlanStatusRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/{id}/copy": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/batch-delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchDeleteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/{id}/items": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TestPlanItemDto"][];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SetPlanItemsRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/{id}/items/from-suite": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ImportPlanItemsRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/{id}/items/validate": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PlanScopeIssueDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/{id}/rounds": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    take?: number;
                };
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PlanRoundSummaryDto"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["StartRoundRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/rounds/{roundId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    roundId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/rounds/{roundId}/abort": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    roundId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/{id}/gate": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PlanGateResult"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/{id}/report": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TestPlanReportDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/test-plans/{id}/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/projects": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    search?: string;
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProjectDtoPagedResult"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateProjectRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/projects/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProjectDto"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateProjectRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/projects/batch-delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchDeleteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/projects/{projectId}/api-tokens": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ApiTokenDto"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateApiTokenRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/projects/{projectId}/api-tokens/{tokenId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                    tokenId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/projects/{projectId}/custom-fields": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CustomFieldDto"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateCustomFieldRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/projects/{projectId}/custom-fields/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/projects/{projectId}/members": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProjectMemberDto"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["AddProjectMemberRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/projects/{projectId}/members/candidates": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    search?: string;
                };
                header?: never;
                path: {
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["UserCandidateDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/projects/{projectId}/members/{userId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                    userId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateProjectMemberRoleRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                    userId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/comments": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    target?: string;
                    targetId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CommentDto"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateCommentRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/comments/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/notifications": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    unreadOnly?: boolean;
                    category?: components["schemas"]["NotificationCategory"];
                    projectId?: string;
                    title?: string;
                    dateFrom?: string;
                    dateTo?: string;
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["NotificationDtoPagedResult"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/notifications/unread-count": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["NotificationUnreadDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/notifications/{id}/read": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/notifications/read-all": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: {
                    category?: components["schemas"]["NotificationCategory"];
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/notifications/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/defects": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                    status?: components["schemas"]["DefectStatus"];
                    severity?: components["schemas"]["DefectSeverity"];
                    assignedToId?: string;
                    executionId?: string;
                    testCaseId?: string;
                    search?: string;
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["DefectListItemDtoPagedResult"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateDefectRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/defects/stats": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["DefectStatsDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/defects/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["DefectDetailDto"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateDefectRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/defects/batch-delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchDeleteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/defects/{id}/transition": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["DefectTransitionRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/defects/{id}/cases": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["LinkDefectCaseRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/defects/{id}/cases/{testCaseId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                    testCaseId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/defects/{id}/occurrences": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["DefectOccurrenceRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/defects/external-providers": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ExternalProviderInfo"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/defects/{id}/push-external": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["PushExternalRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/requirements": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                    search?: string;
                    status?: components["schemas"]["RequirementStatus"];
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RequirementListItemDtoPagedResult"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateRequirementRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/requirements/coverage": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RequirementCoverageDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/requirements/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RequirementListItemDto"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateRequirementRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/requirements/{id}/plans": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RequirementPlanRefDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/requirements/batch-delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchDeleteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/artifacts/image": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "multipart/form-data": {
                        /** Format: binary */
                        file: string;
                    };
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                    reviewStatus?: components["schemas"]["CaseReviewStatus"];
                    search?: string;
                    module?: string;
                    requirementId?: string;
                    flakyOnly?: boolean;
                    execState?: components["schemas"]["CaseExecFilter"];
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TestCaseSummaryDtoPagedResult"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateTestCaseRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/modules": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TestCaseModuleStatDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/import-template": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/import": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TestCaseDto"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateTestCaseRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/{id}/steps": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateTestCaseStepsRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/batch-delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchDeleteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/batch-update": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchUpdateRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/{id}/submit-review": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/{id}/review": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ReviewActionRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/{id}/flake": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SetFlakeRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/reset-flake": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchDeleteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/{id}/versions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TestCaseVersionSummaryDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/{id}/versions/{version}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                    version: number;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TestCaseVersionDetailDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/{id}/versions/{version}/restore": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                    version: number;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/testcases/{id}/versions/batch-delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchDeleteVersionsRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                    testCaseId?: string;
                    status?: components["schemas"]["ExecutionStatus"];
                    days?: number;
                    runningOnly?: boolean;
                    suiteRunId?: string;
                    suiteId?: string;
                    browser?: string;
                    testCaseName?: string;
                    dateFrom?: string;
                    dateTo?: string;
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ExecutionSummaryDtoPagedResult"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateExecutionRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions/batch": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchExecuteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions/batch-delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchDeleteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ExecutionDetailDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions/{id}/agent-attempts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AgentAttemptDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions/agent-attempts/{attemptId}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    attemptId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions/agent-attempts/{attemptId}/reject": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    attemptId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions/{id}/replan": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions/agent-approvals": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    status?: string;
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AgentApprovalItemDtoPagedResult"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions/agent-heal-metrics": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    from?: string;
                    to?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AgentHealMetricsDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions/{id}/defect-links": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ExecutionDefectLinkDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions/{id}/trace": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions/{id}/video": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions/{id}/cancel": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/executions/{id}/diagnose": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/ai/extract-document": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/ai/generate-cases": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["GenerateCasesRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/ai/analyze-api-flow": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["AnalyzeApiFlowRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/ai/adopt": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["AdoptCasesRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/ai/import-swagger": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ImportSwaggerRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/chat/stream": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ChatStreamRequestDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mocks": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MockDto"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateMockRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mocks/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mocks/{id}/start": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/settings": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SettingsView"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateSettingsRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/settings/ai-providers": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AIProviderPreset"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/settings/ai/test": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/settings/notify/test": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["NotifyTestRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/settings/mail/test": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["TestMailRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/stats/dashboard": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    trendDays?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["DashboardResponse"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/reports/executions/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/reports/executions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchReportRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/reports/projects/{projectId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    from?: string;
                    to?: string;
                    planId?: string;
                };
                header?: never;
                path: {
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/schedules": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                    enabled?: boolean;
                    keyword?: string;
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ScheduleSummaryDtoPagedResult"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateScheduleRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/schedules/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ScheduleDetailDto"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateScheduleRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/schedules/{id}/toggle": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/schedules/{id}/run": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/schedules/cron-preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CronPreviewRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/schedules/batch-delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchDeleteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/datasets": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                    keyword?: string;
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["DataSetSummaryDtoPagedResult"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateDataSetRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/datasets/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["DataSetDto"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateDataSetRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/datasets/batch-delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchDeleteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/datasets/import": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/datasets/check/{testCaseId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    testCaseId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CaseVariableCheckDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/datasets/attach/{testCaseId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    testCaseId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["AttachDataSetRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/suites": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                    kind?: components["schemas"]["SuiteKind"];
                    keyword?: string;
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SuiteSummaryDtoPagedResult"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateSuiteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/suites/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SuiteDetailDto"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateSuiteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/suites/batch-delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchDeleteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/suites/{id}/run": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["RunSuiteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/suites/{id}/runs": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    take?: number;
                };
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SuiteRunSummaryDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/suites/{id}/cases": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SetSuiteCasesRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/visual/baselines": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                    testCaseId?: string;
                    keyword?: string;
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["VisualBaselineDtoPagedResult"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/visual/baselines/accept": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["AcceptBaselineRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/visual/baselines/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/visual/cases/{testCaseId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    testCaseId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["VisualCaseSettingDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/shares": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    kind?: components["schemas"]["ReportShareKind"];
                    refId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ShareDto"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateShareRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/shares/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/public/reports/{token}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    token: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/public/reports/{token}/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    token: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/scripts/parse": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ParseScriptRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/scripts/import": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ImportScriptRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/scripts/export/{testCaseId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    testCaseId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ScriptExportDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/loadtests": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    projectId?: string;
                    keyword?: string;
                    source?: number;
                    page?: number;
                    pageSize?: number;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["LoadTestScenarioSummaryDtoPagedResult"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateLoadTestScenarioRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["LoadTestScenarioDetailDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/loadtests/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["LoadTestScenarioDetailDto"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateLoadTestScenarioRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["LoadTestScenarioDetailDto"];
                    };
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/loadtests/batch-delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BatchDeleteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["BatchDeleteResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/loadtests/{id}/generate": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["GenerateScriptResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/loadtests/{id}/script": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/loadtests/{id}/run": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/loadtests/{id}/runs": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    take?: number;
                };
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["LoadTestRunSummaryDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/loadtests/runs/{runId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    runId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["LoadTestRunDetailDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/loadtests/runs/{runId}/summary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    runId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/loadtests/runs/{runId}/log": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    runId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/loadtests/runs/{runId}/cancel": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    runId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/loadtests/import-openapi": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ImportOpenApiRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ImportOpenApiResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/projects/{projectId}/environments": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["EnvironmentView"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateEnvironmentRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/environments/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateEnvironmentRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/webhooks/executions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["WebhookTriggerRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
}
export type webhooks = Record<string, never>;
export interface components {
    schemas: {
        AIModelOption: {
            id?: string;
            label?: string;
            note?: string | null;
        };
        AIProviderPreset: {
            id?: string;
            name?: string;
            baseUrl?: string;
            keyUrl?: string;
            note?: string | null;
            models?: components["schemas"]["AIModelOption"][];
        };
        AcceptBaselineRequest: {
            /** Format: uuid */
            executionResultId?: string;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        ActionType: 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 | 16 | 17 | 18 | 19 | 20 | 21 | 22 | 23;
        AddProjectMemberRequest: {
            /** Format: uuid */
            userId?: string;
            role?: components["schemas"]["ProjectRole"];
        };
        AdoptCaseRequest: {
            name?: string;
            priority?: string;
            type?: components["schemas"]["TestType"];
            steps?: components["schemas"]["CreateTestStepRequest"][];
        };
        AdoptCasesRequest: {
            /** Format: uuid */
            projectId?: string;
            cases?: components["schemas"]["AdoptCaseRequest"][];
            aiPrompt?: string | null;
        };
        AgentApprovalItemDto: {
            /** Format: uuid */
            attemptId?: string;
            /** Format: uuid */
            executionId?: string;
            testCaseName?: string;
            /** Format: int32 */
            targetStepOrder?: number;
            /** Format: int32 */
            fixCategory?: number;
            /** Format: float */
            confidence?: number;
            fixSummary?: string | null;
            approved?: boolean | null;
            /** Format: uuid */
            approvedBy?: string | null;
            /** Format: date-time */
            approvedAt?: string | null;
            /** Format: int32 */
            result?: number;
            /** Format: date-time */
            createdAt?: string;
        };
        AgentApprovalItemDtoPagedResult: {
            items?: components["schemas"]["AgentApprovalItemDto"][];
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
        };
        AgentAttemptDto: {
            /** Format: uuid */
            id?: string;
            /** Format: int32 */
            attemptNumber?: number;
            /** Format: int32 */
            targetStepOrder?: number;
            /** Format: int32 */
            fixCategory?: number;
            /** Format: float */
            confidence?: number;
            fixSummary?: string | null;
            appliedSuccessfully?: boolean;
            appliedActions?: string | null;
            /** Format: int32 */
            result?: number;
            failureAfterFix?: string | null;
            needsApproval?: boolean;
            approved?: boolean | null;
            /** Format: int32 */
            llmInputTokens?: number;
            /** Format: int32 */
            llmOutputTokens?: number;
            llmModel?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            completedAt?: string | null;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        AgentAttemptResult: 0 | 1 | 2 | 3 | 4 | 5;
        AgentHealMetricsDto: {
            /** Format: int32 */
            totalAttempts?: number;
            /** Format: int32 */
            fixed?: number;
            /** Format: int32 */
            partial?: number;
            /** Format: int32 */
            failed?: number;
            /** Format: int32 */
            skipped?: number;
            /** Format: int32 */
            rejected?: number;
            /** Format: int32 */
            budgetExhausted?: number;
            /** Format: int32 */
            other?: number;
            /** Format: double */
            successRate?: number;
            /** Format: double */
            avgFixMinutes?: number | null;
            byCategory?: {
                [key: string]: number;
            };
            /** Format: int32 */
            misjudgedCount?: number;
        };
        AnalyzeApiFlowRequest: {
            endpoints?: {
                [key: string]: unknown;
            }[];
        };
        ApiTokenDto: {
            /** Format: uuid */
            id?: string;
            name?: string;
            prefix?: string;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            expiresAt?: string | null;
            /** Format: date-time */
            revokedAt?: string | null;
            /** Format: date-time */
            lastUsedAt?: string | null;
        };
        AttachDataSetRequest: {
            /** Format: uuid */
            dataSetId?: string | null;
        };
        AuditLogPageDto: {
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
            items?: components["schemas"]["AuditLogViewDto"][];
        };
        AuditLogViewDto: {
            /** Format: uuid */
            id?: string;
            username?: string | null;
            userRole?: string | null;
            action?: string;
            resourceType?: string;
            resourceId?: string | null;
            resourceName?: string | null;
            method?: string;
            path?: string;
            /** Format: int32 */
            statusCode?: number;
            succeeded?: boolean;
            detail?: string | null;
            ipAddress?: string | null;
            /** Format: int32 */
            durationMs?: number;
            /** Format: date-time */
            createdAt?: string;
            responseBody?: string | null;
        };
        BatchDeleteRequest: {
            ids?: string[];
        };
        BatchDeleteResultDto: {
            /** Format: int32 */
            deleted?: number;
            skipped?: components["schemas"]["BatchDeleteSkippedItem"][];
        };
        BatchDeleteSkippedItem: {
            /** Format: uuid */
            id?: string;
            name?: string | null;
            reason?: string;
        };
        BatchDeleteVersionsRequest: {
            versions?: number[];
        };
        BatchExecuteRequest: {
            testCaseIds?: string[];
            /** Format: uuid */
            environmentId?: string | null;
            browsers?: string[] | null;
            expandDataSets?: boolean;
            variables?: {
                [key: string]: string;
            } | null;
        };
        BatchReportRequest: {
            executionIds?: string[];
        };
        BatchUpdateRequest: {
            ids?: string[];
            module?: string | null;
            priority?: string | null;
            status?: components["schemas"]["TestCaseStatus"];
            /** Format: uuid */
            projectId?: string | null;
            /** Format: uuid */
            requirementId?: string | null;
        };
        BrowserOption: {
            id?: string;
            name?: string;
            note?: string;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        CaseExecFilter: 0 | 1 | 2 | 3 | 4 | 5;
        /**
         * Format: int32
         * @enum {integer}
         */
        CaseReviewStatus: 0 | 1 | 2 | 3;
        CaseVariableCheckDto: {
            /** Format: uuid */
            testCaseId?: string;
            caseName?: string;
            /** Format: uuid */
            dataSetId?: string | null;
            dataSetName?: string | null;
            usedVariables?: string[];
            availableColumns?: string[];
            missingVariables?: string[];
            unusedColumns?: string[];
            sampleRow?: {
                [key: string]: string;
            };
        };
        ChangeOwnPasswordRequest: {
            oldPassword?: string;
            newPassword?: string;
        };
        ChatMessageDto: {
            role?: string;
            content?: string;
        };
        ChatStreamRequestDto: {
            messages?: components["schemas"]["ChatMessageDto"][];
            images?: string[] | null;
        };
        CommentDto: {
            /** Format: uuid */
            id?: string;
            authorName?: string;
            body?: string;
            /** Format: date-time */
            createdAt?: string;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        CommentTarget: 0 | 1 | 2;
        CreateApiTokenRequest: {
            name?: string | null;
            /** Format: int32 */
            expiresInDays?: number | null;
        };
        CreateCommentRequest: {
            target?: components["schemas"]["CommentTarget"];
            /** Format: uuid */
            targetId?: string;
            body?: string | null;
        };
        CreateCustomFieldRequest: {
            name?: string | null;
            fieldType?: components["schemas"]["CustomFieldType"];
            options?: string | null;
        };
        CreateDataSetRequest: {
            /** Format: uuid */
            projectId?: string;
            name?: string;
            description?: string | null;
            columns?: string[];
            rows?: {
                [key: string]: string;
            }[];
            firstRowIsSample?: boolean;
        };
        CreateDefectRequest: {
            /** Format: uuid */
            projectId?: string;
            title?: string;
            description?: string | null;
            severity?: components["schemas"]["DefectSeverity"];
            /** Format: uuid */
            assignedToId?: string | null;
            externalRef?: string | null;
            /** Format: uuid */
            foundInExecutionId?: string | null;
            /** Format: int32 */
            foundInStepOrder?: number | null;
            testCaseIds?: string[] | null;
        };
        CreateEnvironmentRequest: {
            name?: string;
            baseUrl?: string;
            loginUrl?: string | null;
            loginUsername?: string | null;
            loginPassword?: string | null;
            loginSuccessIndicator?: string | null;
            autoLogin?: boolean;
            browser?: string | null;
        };
        CreateExecutionRequest: {
            /** Format: uuid */
            testCaseId?: string;
            /** Format: uuid */
            environmentId?: string | null;
            browser?: string | null;
            /** Format: int32 */
            dataSetRowIndex?: number | null;
            variables?: {
                [key: string]: string;
            } | null;
        };
        CreateLoadTestScenarioRequest: {
            /** Format: uuid */
            projectId?: string;
            name?: string;
            description?: string | null;
            source?: components["schemas"]["LoadTestSource"];
            /** Format: uuid */
            environmentId?: string | null;
            targetBaseUrl?: string | null;
            /** Format: uuid */
            apiDefinitionId?: string | null;
            operations?: string[] | null;
            profile?: components["schemas"]["LoadTestProfile"];
            thresholds?: components["schemas"]["LoadTestThreshold"][] | null;
            variables?: {
                [key: string]: string;
            } | null;
            caseIds?: string[] | null;
        };
        CreateMockRequest: {
            /** Format: uuid */
            projectId?: string;
            name?: string;
            spec?: string;
        };
        CreateProjectRequest: {
            name?: string;
            description?: string | null;
            /** Format: uuid */
            managerId?: string | null;
            /** Format: uuid */
            testOwnerId?: string | null;
            /** Format: uuid */
            developerOwnerId?: string | null;
        };
        CreateRecorderSessionRequest: {
            /** Format: uuid */
            projectId?: string;
            name?: string | null;
            baseUrl?: string | null;
            browser?: string | null;
        };
        CreateRequirementRequest: {
            /** Format: uuid */
            projectId?: string;
            title?: string;
            description?: string | null;
            externalKey?: string | null;
            priority?: string | null;
            /** Format: date-time */
            planStartDate?: string | null;
            /** Format: date-time */
            planEndDate?: string | null;
            /** Format: date-time */
            actualStartDate?: string | null;
            /** Format: date-time */
            actualEndDate?: string | null;
            status?: components["schemas"]["RequirementStatus"];
        };
        CreateScheduleRequest: {
            /** Format: uuid */
            projectId?: string;
            name?: string;
            cronExpression?: string;
            enabled?: boolean;
            module?: string | null;
            priority?: string | null;
            testCaseIds?: string[] | null;
            /** Format: uuid */
            environmentId?: string | null;
            browsers?: string[] | null;
            expandDataSets?: boolean;
            scopeKind?: components["schemas"]["ScheduleScopeKind"];
            testPlanIds?: string[] | null;
        };
        CreateShareRequest: {
            kind?: components["schemas"]["ReportShareKind"];
            /** Format: uuid */
            refId?: string;
            /** Format: uuid */
            projectId?: string | null;
            title?: string | null;
            /** Format: date-time */
            from?: string | null;
            /** Format: date-time */
            to?: string | null;
            /** Format: int32 */
            expiresInDays?: number | null;
        };
        CreateSuiteRequest: {
            /** Format: uuid */
            projectId?: string;
            name?: string;
            description?: string | null;
            kind?: components["schemas"]["SuiteKind"];
            /** Format: uuid */
            environmentId?: string | null;
            cases?: components["schemas"]["SuiteCaseSpec"][] | null;
            failurePolicy?: components["schemas"]["SuiteFailurePolicy"];
        };
        CreateTestCaseRequest: {
            /** Format: uuid */
            projectId?: string;
            name?: string;
            type?: components["schemas"]["TestType"];
            description?: string | null;
            browser?: string | null;
            /** Format: int32 */
            timeout?: number;
            /** Format: int32 */
            retryCount?: number;
            steps?: components["schemas"]["CreateTestStepRequest"][];
            baseUrl?: string | null;
            failFast?: boolean;
            caseCode?: string | null;
            module?: string | null;
            sourceSteps?: string | null;
            expectedResult?: string | null;
            priority?: string | null;
            visualEnabled?: boolean;
            /** Format: double */
            visualThreshold?: number | null;
            visualIgnoreRegions?: string | null;
            customFields?: string | null;
            /** Format: uuid */
            dataSetId?: string | null;
            reviewStatus?: components["schemas"]["CaseReviewStatus"];
            /** Format: date-time */
            reviewedAt?: string | null;
            reviewNote?: string | null;
            reviewedByName?: string | null;
            /** Format: uuid */
            requirementId?: string | null;
            networkRules?: string | null;
        };
        CreateTestPlanRequest: {
            /** Format: uuid */
            projectId?: string;
            name?: string;
            description?: string | null;
            releaseName?: string | null;
            /** Format: date-time */
            startsAt?: string | null;
            /** Format: date-time */
            endsAt?: string | null;
            /** Format: uuid */
            ownerId?: string | null;
            /** Format: double */
            targetPassRate?: number | null;
            allowErrors?: boolean | null;
            excludeFlakyFromFailure?: boolean | null;
            gateMode?: components["schemas"]["PlanGateMode"];
            defectGateEnabled?: boolean | null;
            /** Format: uuid */
            environmentId?: string | null;
            browsers?: string[] | null;
            expandDataSets?: boolean | null;
            testCaseIds?: string[] | null;
            /** Format: uuid */
            requirementId?: string | null;
        };
        CreateTestStepRequest: {
            /** Format: int32 */
            stepOrder?: number;
            actionType?: components["schemas"]["ActionType"];
            config?: components["schemas"]["StepConfig"];
            aiInstruction?: string | null;
            aiElementDescription?: string | null;
            /** Format: uuid */
            sharedGroupId?: string | null;
            sharedVariables?: components["schemas"]["SharedVariableEntry"][] | null;
        };
        CreateUserRequest: {
            username?: string;
            password?: string;
            displayName?: string;
            role?: components["schemas"]["UserRole"];
            email?: string | null;
        };
        CronPreviewRequest: {
            cronExpression?: string;
            /** Format: int32 */
            count?: number;
        };
        CustomFieldDto: {
            /** Format: uuid */
            id?: string;
            name?: string;
            fieldType?: components["schemas"]["CustomFieldType"];
            options?: string | null;
            /** Format: date-time */
            createdAt?: string;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        CustomFieldType: 0 | 1 | 2 | 3;
        DashboardOverview: {
            /** Format: int32 */
            totalProjects?: number;
            /** Format: int32 */
            totalCases?: number;
            /** Format: int32 */
            totalExecutions?: number;
            /** Format: int32 */
            executions7d?: number;
            /** Format: double */
            passRate7d?: number;
            /** Format: int32 */
            runningCount?: number;
            /** Format: int32 */
            flakyCount?: number;
            /** Format: int32 */
            totalPlans?: number;
            /** Format: int32 */
            activePlans?: number;
            /** Format: int32 */
            durationP95Ms?: number;
            /** Format: int32 */
            durationAvgMs?: number;
            /** Format: int32 */
            openDefects?: number;
            /** Format: int32 */
            openCriticalDefects?: number;
            /** Format: int32 */
            newDefects7d?: number;
            /** Format: int32 */
            closedDefects7d?: number;
        };
        DashboardResponse: {
            overview?: components["schemas"]["DashboardOverview"];
            trend?: components["schemas"]["TrendPoint"][];
            unstableTop?: components["schemas"]["UnstableCaseItem"][];
            activePlans?: components["schemas"]["PlanGatingItem"][];
            scheduleHealth?: components["schemas"]["ScheduleHealth"];
        };
        DataSet: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            project?: components["schemas"]["Project"];
            name?: string;
            description?: string | null;
            columns?: string[];
            rows?: {
                [key: string]: string;
            }[];
            /** Format: int32 */
            rowCount?: number;
            firstRowIsSample?: boolean;
            /** Format: uuid */
            createdById?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: uuid */
            updatedById?: string | null;
            /** Format: date-time */
            updatedAt?: string;
        };
        DataSetDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            name?: string;
            description?: string | null;
            columns?: string[];
            rows?: {
                [key: string]: string;
            }[];
            firstRowIsSample?: boolean;
            /** Format: int32 */
            usedByCaseCount?: number;
            usedBy?: components["schemas"]["DataSetUsageDto"][];
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string;
        };
        DataSetSummaryDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            projectName?: string | null;
            name?: string;
            description?: string | null;
            /** Format: int32 */
            columnCount?: number;
            /** Format: int32 */
            rowCount?: number;
            /** Format: int32 */
            usedByCaseCount?: number;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string;
            createdByName?: string | null;
        };
        DataSetSummaryDtoPagedResult: {
            items?: components["schemas"]["DataSetSummaryDto"][];
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
        };
        DataSetUsageDto: {
            /** Format: uuid */
            testCaseId?: string;
            name?: string;
            module?: string | null;
        };
        DefectCaseLinkDto: {
            /** Format: uuid */
            testCaseId?: string;
            testCaseName?: string;
            module?: string | null;
        };
        DefectDetailDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            projectName?: string;
            title?: string;
            description?: string | null;
            severity?: components["schemas"]["DefectSeverity"];
            status?: components["schemas"]["DefectStatus"];
            /** Format: uuid */
            assignedToId?: string | null;
            assignedToName?: string | null;
            /** Format: uuid */
            createdById?: string | null;
            createdByName?: string | null;
            /** Format: uuid */
            foundInExecutionId?: string | null;
            /** Format: int32 */
            foundInStepOrder?: number | null;
            /** Format: uuid */
            foundInTestCaseId?: string | null;
            foundInTestCaseName?: string | null;
            externalRef?: string | null;
            resolutionNote?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string;
            /** Format: date-time */
            fixedAt?: string | null;
            /** Format: date-time */
            verifiedAt?: string | null;
            verifiedByName?: string | null;
            cases?: components["schemas"]["DefectCaseLinkDto"][];
            occurrences?: components["schemas"]["DefectOccurrenceDto"][];
        };
        DefectListItemDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            projectName?: string;
            title?: string;
            severity?: components["schemas"]["DefectSeverity"];
            status?: components["schemas"]["DefectStatus"];
            assignedToName?: string | null;
            createdByName?: string | null;
            /** Format: uuid */
            foundInExecutionId?: string | null;
            /** Format: int32 */
            foundInStepOrder?: number | null;
            /** Format: uuid */
            foundInTestCaseId?: string | null;
            foundInTestCaseName?: string | null;
            externalRef?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            fixedAt?: string | null;
            /** Format: date-time */
            verifiedAt?: string | null;
        };
        DefectListItemDtoPagedResult: {
            items?: components["schemas"]["DefectListItemDto"][];
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
        };
        DefectOccurrenceDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            executionId?: string | null;
            /** Format: int32 */
            stepOrder?: number;
            /** Format: date-time */
            occurredAt?: string;
        };
        DefectOccurrenceRequest: {
            /** Format: uuid */
            executionId?: string;
            /** Format: int32 */
            stepOrder?: number;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        DefectSeverity: 0 | 1 | 2 | 3;
        DefectStatsDto: {
            /** Format: int32 */
            openTotal?: number;
            /** Format: int32 */
            openCritical?: number;
            /** Format: int32 */
            openMajor?: number;
            /** Format: int32 */
            openNormal?: number;
            /** Format: int32 */
            openSuggestion?: number;
            /** Format: int32 */
            createdLast7Days?: number;
            /** Format: int32 */
            closedLast7Days?: number;
            /** Format: double */
            avgFixHours?: number | null;
            /** Format: double */
            avgVerifyHours?: number | null;
            trend?: components["schemas"]["DefectTrendPoint"][];
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        DefectStatus: 0 | 1 | 2 | 3 | 4 | 5 | 6;
        DefectTransitionRequest: {
            action?: string;
            /** Format: uuid */
            assignedToId?: string | null;
            note?: string | null;
        };
        DefectTrendPoint: {
            date?: string;
            /** Format: int32 */
            created?: number;
            /** Format: int32 */
            closed?: number;
        };
        Environment: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            project?: components["schemas"]["Project"];
            name?: string;
            baseUrl?: string;
            loginUrl?: string | null;
            loginUsername?: string | null;
            loginPassword?: string | null;
            loginSuccessIndicator?: string | null;
            autoLogin?: boolean;
            browser?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string;
        };
        EnvironmentSnapshot: {
            name?: string;
            baseUrl?: string;
        };
        EnvironmentView: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            name?: string;
            baseUrl?: string;
            loginUrl?: string | null;
            loginUsername?: string | null;
            loginPasswordMasked?: string;
            hasLoginPassword?: boolean;
            loginSuccessIndicator?: string | null;
            autoLogin?: boolean;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string;
            browser?: string | null;
        };
        Execution: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            testCaseId?: string | null;
            testCase?: components["schemas"]["TestCase"];
            status?: components["schemas"]["ExecutionStatus"];
            /** Format: uuid */
            triggeredById?: string | null;
            triggeredBy?: components["schemas"]["User"];
            triggerType?: components["schemas"]["TriggerType"];
            commitSha?: string | null;
            branch?: string | null;
            buildNumber?: string | null;
            triggerSource?: string | null;
            claimedBy?: string | null;
            /** Format: date-time */
            heartbeatAt?: string | null;
            browserName?: string | null;
            browserVersion?: string | null;
            /** Format: int32 */
            dataSetRowIndex?: number | null;
            dataSetRowLabel?: string | null;
            variables?: {
                [key: string]: string;
            } | null;
            /** Format: uuid */
            suiteId?: string | null;
            /** Format: uuid */
            suiteRunId?: string | null;
            /** Format: uuid */
            dependsOnTestCaseId?: string | null;
            skipReason?: string | null;
            /** Format: uuid */
            planId?: string | null;
            /** Format: uuid */
            planRoundId?: string | null;
            /** Format: date-time */
            startedAt?: string | null;
            /** Format: date-time */
            endedAt?: string | null;
            /** Format: int32 */
            durationMs?: number | null;
            traceUrl?: string | null;
            /** Format: int64 */
            traceSizeBytes?: number | null;
            videoUrl?: string | null;
            /** Format: int64 */
            videoSizeBytes?: number | null;
            aiDiagnosis?: string | null;
            aiSuggestedFix?: string | null;
            /** Format: float */
            diagnosisConfidence?: number | null;
            /** Format: int32 */
            agentLoopCount?: number;
            agentHealed?: boolean;
            /** Format: int32 */
            agentBudgetUsed?: number;
            agentFinalVerdict?: components["schemas"]["AgentAttemptResult"];
            /** Format: int32 */
            stepRetryCount?: number;
            /** Format: int32 */
            totalSteps?: number | null;
            /** Format: uuid */
            environmentId?: string | null;
            environment?: components["schemas"]["Environment"];
            environmentSnapshot?: components["schemas"]["EnvironmentSnapshot"];
            results?: components["schemas"]["ExecutionResult"][];
            /** Format: date-time */
            createdAt?: string;
        };
        ExecutionDefectLinkDto: {
            /** Format: int32 */
            stepOrder?: number;
            /** Format: uuid */
            defectId?: string;
            defectTitle?: string;
            status?: components["schemas"]["DefectStatus"];
        };
        ExecutionDetailDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            testCaseId?: string | null;
            testCaseName?: string;
            status?: components["schemas"]["ExecutionStatus"];
            triggerType?: components["schemas"]["TriggerType"];
            browserVersion?: string | null;
            /** Format: date-time */
            startedAt?: string | null;
            /** Format: date-time */
            endedAt?: string | null;
            /** Format: int32 */
            durationMs?: number | null;
            /** Format: date-time */
            createdAt?: string;
            aiDiagnosis?: string | null;
            aiSuggestedFix?: string | null;
            /** Format: float */
            diagnosisConfidence?: number | null;
            results?: components["schemas"]["ExecutionResultDto"][];
            environmentName?: string | null;
            /** Format: uuid */
            environmentId?: string | null;
            browserName?: string | null;
            dataSetRowLabel?: string | null;
            /** Format: int32 */
            dataSetRowIndex?: number | null;
            /** Format: uuid */
            suiteId?: string | null;
            /** Format: uuid */
            suiteRunId?: string | null;
            traceUrl?: string | null;
            /** Format: int64 */
            traceSizeBytes?: number | null;
            /** Format: int32 */
            stepRetryCount?: number;
            /** Format: uuid */
            dependsOnTestCaseId?: string | null;
            skipReason?: string | null;
            /** Format: int32 */
            totalSteps?: number | null;
            projectName?: string | null;
            videoUrl?: string | null;
            /** Format: int64 */
            videoSizeBytes?: number | null;
            agentHealed?: boolean;
            /** Format: int32 */
            agentFinalVerdict?: number | null;
            /** Format: int32 */
            agentLoopCount?: number;
            /** Format: int32 */
            agentBudgetUsed?: number;
        };
        ExecutionResult: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            executionId?: string;
            execution?: components["schemas"]["Execution"];
            /** Format: uuid */
            testStepId?: string | null;
            testStep?: components["schemas"]["TestStep"];
            /** Format: int32 */
            stepOrder?: number;
            status?: components["schemas"]["ExecutionStatus"];
            /** Format: int32 */
            durationMs?: number | null;
            screenshotUrl?: string | null;
            log?: string | null;
            errorMessage?: string | null;
            stackTrace?: string | null;
            stepSnapshot?: components["schemas"]["StepConfig"];
            /** Format: int32 */
            stepActionType?: number | null;
            visualStatus?: components["schemas"]["VisualStatus"];
            /** Format: double */
            visualDiffRatio?: number | null;
            /** Format: double */
            visualThreshold?: number | null;
            baselineImageUrl?: string | null;
            diffImageUrl?: string | null;
            visualNote?: string | null;
            elementSnapshot?: string | null;
            /** Format: date-time */
            createdAt?: string;
        };
        ExecutionResultDto: {
            /** Format: uuid */
            id?: string;
            /** Format: int32 */
            stepOrder?: number;
            status?: components["schemas"]["ExecutionStatus"];
            /** Format: int32 */
            durationMs?: number | null;
            screenshotUrl?: string | null;
            log?: string | null;
            errorMessage?: string | null;
            stackTrace?: string | null;
            stepSnapshot?: components["schemas"]["StepConfig"];
            /** Format: uuid */
            testStepId?: string | null;
            /** Format: int32 */
            stepActionType?: number | null;
            visualStatus?: components["schemas"]["VisualStatus"];
            /** Format: double */
            visualDiffRatio?: number | null;
            baselineImageUrl?: string | null;
            diffImageUrl?: string | null;
            visualNote?: string | null;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        ExecutionStatus: 0 | 1 | 2 | 3 | 4 | 5 | 6;
        ExecutionSummaryDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            testCaseId?: string | null;
            testCaseName?: string;
            status?: components["schemas"]["ExecutionStatus"];
            triggerType?: components["schemas"]["TriggerType"];
            browserVersion?: string | null;
            /** Format: date-time */
            startedAt?: string | null;
            /** Format: date-time */
            endedAt?: string | null;
            /** Format: int32 */
            durationMs?: number | null;
            /** Format: int32 */
            resultCount?: number;
            /** Format: date-time */
            createdAt?: string;
            aiDiagnosis?: string | null;
            environmentName?: string | null;
            /** Format: uuid */
            environmentId?: string | null;
            triggerSource?: string | null;
            commitSha?: string | null;
            branch?: string | null;
            buildNumber?: string | null;
            browserName?: string | null;
            dataSetRowLabel?: string | null;
            /** Format: uuid */
            suiteId?: string | null;
            /** Format: uuid */
            suiteRunId?: string | null;
            traceUrl?: string | null;
            /** Format: int64 */
            traceSizeBytes?: number | null;
            /** Format: uuid */
            dependsOnTestCaseId?: string | null;
            skipReason?: string | null;
            videoUrl?: string | null;
            /** Format: int64 */
            videoSizeBytes?: number | null;
            projectName?: string | null;
            agentHealed?: boolean;
        };
        ExecutionSummaryDtoPagedResult: {
            items?: components["schemas"]["ExecutionSummaryDto"][];
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
        };
        ExternalProviderInfo: {
            id?: string;
            name?: string;
            browseBaseUrl?: string;
        };
        GenerateCasesRequest: {
            requirement?: string;
            /** Format: int32 */
            minCases?: number;
        };
        GenerateScriptResultDto: {
            script?: string;
            hash?: string;
            warnings?: string[];
        };
        HeaderEntry: {
            name?: string;
            value?: string;
        };
        ImportOpenApiRequest: {
            /** Format: uuid */
            projectId?: string;
            name?: string;
            spec?: string;
        };
        ImportOpenApiResultDto: {
            /** Format: uuid */
            apiDefinitionId?: string;
            apiName?: string;
            baseUrl?: string;
            operations?: components["schemas"]["OpenApiOperationDto"][];
        };
        ImportPlanItemsRequest: {
            /** Format: uuid */
            suiteId?: string;
            mode?: string;
        };
        ImportScriptRequest: {
            /** Format: uuid */
            projectId?: string;
            name?: string;
            script?: string;
            module?: string | null;
            priority?: string | null;
            baseUrl?: string | null;
            description?: string | null;
            skipUnsupportedLines?: boolean;
            /** Format: uuid */
            testPlanId?: string | null;
        };
        ImportSwaggerRequest: {
            /** Format: uuid */
            projectId?: string;
            url?: string | null;
            content?: string | null;
        };
        LinkDefectCaseRequest: {
            /** Format: uuid */
            testCaseId?: string;
        };
        LoadTestProfile: {
            kind?: string;
            /** Format: int32 */
            vus?: number;
            /** Format: int32 */
            rate?: number;
            timeUnit?: string;
            /** Format: int32 */
            preAllocatedVUs?: number;
            /** Format: int32 */
            maxVUs?: number;
            stages?: components["schemas"]["LoadTestStage"][];
            duration?: string;
            gracefulRampDown?: string;
            /** Format: double */
            thinkTimeSeconds?: number;
        };
        LoadTestRunDetailDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            scenarioId?: string;
            scenarioName?: string;
            /** Format: uuid */
            projectId?: string;
            /** Format: int32 */
            status?: number;
            targetBaseUrl?: string;
            /** Format: date-time */
            startedAt?: string | null;
            /** Format: date-time */
            endedAt?: string | null;
            /** Format: int32 */
            durationMs?: number | null;
            k6Version?: string | null;
            /** Format: int32 */
            exitCode?: number | null;
            errorMessage?: string | null;
            /** Format: int64 */
            totalRequests?: number;
            /** Format: double */
            rps?: number | null;
            /** Format: double */
            avgMs?: number | null;
            /** Format: double */
            p50Ms?: number | null;
            /** Format: double */
            p95Ms?: number | null;
            /** Format: double */
            p99Ms?: number | null;
            /** Format: double */
            maxMs?: number | null;
            /** Format: double */
            errorRate?: number | null;
            /** Format: double */
            checksRate?: number | null;
            /** Format: int64 */
            iterations?: number;
            /** Format: int32 */
            vusMax?: number | null;
            thresholdsPassed?: boolean | null;
            /** Format: int32 */
            thresholdTotal?: number;
            /** Format: int32 */
            thresholdFailed?: number;
            thresholdResults?: components["schemas"]["LoadTestThresholdResult"][];
            hasSummary?: boolean;
            hasLog?: boolean;
            /** Format: date-time */
            createdAt?: string;
        };
        LoadTestRunSummaryDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            scenarioId?: string;
            scenarioName?: string;
            /** Format: int32 */
            status?: number;
            targetBaseUrl?: string;
            /** Format: date-time */
            startedAt?: string | null;
            /** Format: date-time */
            endedAt?: string | null;
            /** Format: int32 */
            durationMs?: number | null;
            /** Format: int64 */
            totalRequests?: number;
            /** Format: double */
            rps?: number | null;
            /** Format: double */
            p95Ms?: number | null;
            /** Format: double */
            p99Ms?: number | null;
            /** Format: double */
            errorRate?: number | null;
            thresholdsPassed?: boolean | null;
            /** Format: int32 */
            thresholdTotal?: number;
            /** Format: int32 */
            thresholdFailed?: number;
            errorMessage?: string | null;
            /** Format: date-time */
            createdAt?: string;
        };
        LoadTestScenarioDetailDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            projectName?: string | null;
            name?: string;
            description?: string | null;
            /** Format: int32 */
            source?: number;
            /** Format: uuid */
            environmentId?: string | null;
            targetBaseUrl?: string | null;
            /** Format: uuid */
            apiDefinitionId?: string | null;
            operations?: string[];
            profile?: components["schemas"]["LoadTestProfile"];
            thresholds?: components["schemas"]["LoadTestThreshold"][];
            variables?: {
                [key: string]: string;
            };
            /** Format: int32 */
            virtualUsers?: number;
            /** Format: int32 */
            durationSeconds?: number;
            scriptText?: string | null;
            scriptHash?: string | null;
            /** Format: date-time */
            scriptGeneratedAt?: string | null;
            caseIds?: string[];
            /** Format: uuid */
            createdById?: string | null;
            createdByName?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string | null;
        };
        LoadTestScenarioSummaryDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            projectName?: string | null;
            name?: string;
            description?: string | null;
            /** Format: int32 */
            source?: number;
            /** Format: int32 */
            virtualUsers?: number;
            /** Format: int32 */
            durationSeconds?: number;
            /** Format: int32 */
            caseCount?: number;
            scriptHash?: string | null;
            /** Format: date-time */
            scriptGeneratedAt?: string | null;
            /** Format: int32 */
            lastRunStatus?: number | null;
            /** Format: date-time */
            lastRunAt?: string | null;
            /** Format: double */
            lastP95Ms?: number | null;
            /** Format: double */
            lastErrorRate?: number | null;
            /** Format: uuid */
            createdById?: string | null;
            createdByName?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string | null;
        };
        LoadTestScenarioSummaryDtoPagedResult: {
            items?: components["schemas"]["LoadTestScenarioSummaryDto"][];
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        LoadTestSource: 0 | 1;
        LoadTestStage: {
            duration?: string;
            /** Format: int32 */
            target?: number;
        };
        LoadTestThreshold: {
            metric?: string;
            aggregator?: string;
            operator?: string;
            /** Format: double */
            value?: number;
        };
        LoadTestThresholdResult: {
            metric?: string;
            expression?: string;
            ok?: boolean;
        };
        LoginRequest: {
            username?: string;
            password?: string;
        };
        MockDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            name?: string;
            /** Format: int32 */
            port?: number | null;
            status?: components["schemas"]["MockStatus"];
            /** Format: date-time */
            createdAt?: string;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        MockStatus: 0 | 1;
        MySsoBindingDto: {
            ssoProvider?: string | null;
        };
        NodeListResponseDto: {
            nodes?: components["schemas"]["NodeViewDto"][];
            /** Format: int32 */
            onlineCount?: number;
            /** Format: int32 */
            offlineCount?: number;
        };
        NodeViewDto: {
            /** Format: uuid */
            id?: string;
            name?: string;
            machineName?: string;
            instanceId?: string;
            version?: string;
            /** Format: int32 */
            maxConcurrency?: number;
            /** Format: int32 */
            runningCount?: number;
            online?: boolean;
            lastHeartbeatAt?: string | null;
            startedAt?: string;
            /** Format: int32 */
            todayCompleted?: number;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        NotificationCategory: 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8;
        NotificationCategoryCount: {
            /** Format: int32 */
            category?: number;
            /** Format: int32 */
            count?: number;
        };
        NotificationDto: {
            /** Format: uuid */
            id?: string;
            /** Format: int32 */
            category?: number;
            /** Format: int32 */
            level?: number;
            title?: string;
            body?: string | null;
            linkUrl?: string | null;
            linkLabel?: string | null;
            sourceType?: string | null;
            /** Format: uuid */
            sourceId?: string | null;
            /** Format: uuid */
            projectId?: string | null;
            isRead?: boolean;
            /** Format: date-time */
            createdAt?: string;
            /** Format: uuid */
            userId?: string;
            userName?: string | null;
            projectName?: string | null;
        };
        NotificationDtoPagedResult: {
            items?: components["schemas"]["NotificationDto"][];
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
        };
        NotificationUnreadDto: {
            /** Format: int32 */
            total?: number;
            byCategory?: components["schemas"]["NotificationCategoryCount"][];
        };
        NotifyChannelView: {
            webhookMasked?: string;
            configured?: boolean;
        };
        NotifyTestRequest: {
            channel?: string | null;
        };
        OpenApiOperationDto: {
            method?: string;
            path?: string;
            label?: string;
        };
        ParseScriptRequest: {
            script?: string;
        };
        ParsedStepDto: {
            /** Format: int32 */
            stepOrder?: number;
            actionType?: components["schemas"]["ActionType"];
            config?: components["schemas"]["StepConfig"];
            instruction?: string | null;
            description?: string | null;
            sourceLine?: string;
            note?: string | null;
        };
        PlanBlockingCaseDto: {
            /** Format: uuid */
            testCaseId?: string;
            name?: string;
            module?: string | null;
            status?: components["schemas"]["ExecutionStatus"];
            errorMessage?: string | null;
            isFlaky?: boolean;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        PlanGateMode: 0 | 1;
        PlanGateResult: {
            passed?: boolean;
            planName?: string;
            releaseName?: string | null;
            /** Format: double */
            targetPassRate?: number;
            /** Format: int32 */
            evaluatedRoundNo?: number | null;
            stats?: components["schemas"]["PlanStatsDto"];
            reasons?: string[];
            blockingCases?: components["schemas"]["PlanBlockingCaseDto"][];
        };
        PlanGatingItem: {
            /** Format: uuid */
            planId?: string;
            /** Format: uuid */
            projectId?: string;
            planName?: string;
            releaseName?: string | null;
            projectName?: string;
            /** Format: double */
            targetPassRate?: number;
            /** Format: int32 */
            caseCount?: number;
            /** Format: int32 */
            evaluatedRoundNo?: number | null;
            gatePassed?: boolean;
            /** Format: double */
            evaluatedPassRate?: number;
            gateReasons?: string[];
            /** Format: date-time */
            endsAt?: string | null;
            /** Format: int32 */
            daysToDeadline?: number | null;
        };
        PlanModuleStatDto: {
            module?: string;
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            passed?: number;
            /** Format: int32 */
            failed?: number;
            /** Format: int32 */
            error?: number;
            /** Format: int32 */
            skipped?: number;
            /** Format: double */
            passRate?: number;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        PlanRoundStatus: 0 | 1 | 2;
        PlanRoundSummaryDto: {
            /** Format: uuid */
            id?: string;
            /** Format: int32 */
            roundNo?: number;
            status?: components["schemas"]["PlanRoundStatus"];
            triggerType?: components["schemas"]["TriggerType"];
            triggerSource?: string | null;
            /** Format: date-time */
            startedAt?: string;
            /** Format: date-time */
            completedAt?: string | null;
            /** Format: int32 */
            createdCount?: number;
            error?: string | null;
            stats?: components["schemas"]["PlanStatsDto"];
            gatePassed?: boolean | null;
        };
        PlanRoundTrendDto: {
            /** Format: int32 */
            roundNo?: number;
            /** Format: date-time */
            startedAt?: string;
            /** Format: date-time */
            completedAt?: string | null;
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            passed?: number;
            /** Format: int32 */
            failed?: number;
            /** Format: int32 */
            error?: number;
            /** Format: int32 */
            skipped?: number;
            /** Format: double */
            passRate?: number;
            gatePassed?: boolean | null;
        };
        PlanScheduleRefDto: {
            /** Format: uuid */
            id?: string;
            name?: string;
            cronExpression?: string;
            enabled?: boolean;
        };
        PlanScopeIssueDto: {
            level?: string;
            kind?: string;
            /** Format: uuid */
            testCaseId?: string | null;
            name?: string;
            message?: string;
        };
        PlanStatsDto: {
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            passed?: number;
            /** Format: int32 */
            failed?: number;
            /** Format: int32 */
            error?: number;
            /** Format: int32 */
            skipped?: number;
            /** Format: int32 */
            pending?: number;
            /** Format: double */
            passRate?: number;
            /** Format: int32 */
            passedNative?: number;
            /** Format: int32 */
            passedViaAgent?: number;
        };
        Project: {
            /** Format: uuid */
            id?: string;
            name?: string;
            description?: string | null;
            /** Format: uuid */
            createdById?: string;
            createdBy?: components["schemas"]["User"];
            /** Format: uuid */
            managerId?: string | null;
            manager?: components["schemas"]["User"];
            /** Format: uuid */
            testOwnerId?: string | null;
            testOwner?: components["schemas"]["User"];
            /** Format: uuid */
            developerOwnerId?: string | null;
            developerOwner?: components["schemas"]["User"];
            agentLoopEnabled?: boolean;
            /** Format: date-time */
            agentLoopSuspendedAt?: string | null;
            treatAgentHealedAsPass?: boolean;
            /** Format: uuid */
            updatedById?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string;
            testCases?: components["schemas"]["TestCase"][];
            environments?: components["schemas"]["Environment"][];
        };
        ProjectDto: {
            /** Format: uuid */
            id?: string;
            name?: string;
            description?: string | null;
            /** Format: uuid */
            createdById?: string;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string;
            /** Format: int32 */
            testCaseCount?: number;
            /** Format: uuid */
            managerId?: string | null;
            managerName?: string | null;
            /** Format: uuid */
            testOwnerId?: string | null;
            testOwnerName?: string | null;
            testOwnerEmail?: string | null;
            /** Format: uuid */
            developerOwnerId?: string | null;
            developerOwnerName?: string | null;
            agentLoopEnabled?: boolean;
            treatAgentHealedAsPass?: boolean;
            createdByName?: string | null;
        };
        ProjectDtoPagedResult: {
            items?: components["schemas"]["ProjectDto"][];
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
        };
        ProjectMemberDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            userId?: string;
            username?: string;
            displayName?: string | null;
            role?: components["schemas"]["ProjectRole"];
            /** Format: date-time */
            createdAt?: string;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        ProjectRole: 0 | 1 | 2 | 3;
        PushExternalRequest: {
            provider?: string;
        };
        RecorderCapabilitiesDto: {
            available?: boolean;
            reason?: string | null;
            browsers?: components["schemas"]["BrowserOption"][];
        };
        RecorderSession: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            project?: components["schemas"]["Project"];
            name?: string;
            baseUrl?: string | null;
            browser?: string;
            status?: components["schemas"]["RecorderStatus"];
            /** Format: int32 */
            processId?: number;
            outputPath?: string;
            /** Format: int32 */
            stepCount?: number;
            scriptFingerprint?: string | null;
            lastError?: string | null;
            /** Format: uuid */
            createdById?: string | null;
            createdByName?: string | null;
            /** Format: uuid */
            savedTestCaseId?: string | null;
            savedTestCaseName?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            startedAt?: string | null;
            /** Format: date-time */
            stoppedAt?: string | null;
            /** Format: date-time */
            lastPolledAt?: string | null;
        };
        RecorderSnapshot: {
            /** Format: uuid */
            sessionId?: string;
            status?: components["schemas"]["RecorderStatus"];
            changed?: boolean;
            script?: string;
            /** Format: int32 */
            stepCount?: number;
            steps?: components["schemas"]["ParsedStepDto"][];
            warnings?: string[];
            baseUrl?: string | null;
            lastError?: string | null;
            /** Format: uuid */
            savedTestCaseId?: string | null;
            savedTestCaseName?: string | null;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        RecorderStatus: 0 | 1 | 2 | 3;
        /**
         * Format: int32
         * @enum {integer}
         */
        ReportShareKind: 0 | 1 | 2 | 3;
        Requirement: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            project?: components["schemas"]["Project"];
            title?: string;
            description?: string | null;
            externalKey?: string | null;
            priority?: string | null;
            /** Format: date-time */
            planStartDate?: string | null;
            /** Format: date-time */
            planEndDate?: string | null;
            /** Format: date-time */
            actualStartDate?: string | null;
            /** Format: date-time */
            actualEndDate?: string | null;
            status?: components["schemas"]["RequirementStatus"];
            /** Format: uuid */
            createdById?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: uuid */
            updatedById?: string | null;
            /** Format: date-time */
            updatedAt?: string;
            testCases?: components["schemas"]["TestCase"][];
            testPlans?: components["schemas"]["TestPlan"][];
        };
        RequirementCoverageDto: {
            /** Format: uuid */
            projectId?: string;
            /** Format: int32 */
            totalRequirements?: number;
            /** Format: int32 */
            coveredRequirements?: number;
            /** Format: int32 */
            uncoveredRequirements?: number;
            /** Format: double */
            coverageRate?: number;
            uncoveredList?: components["schemas"]["RequirementListItemDto"][];
        };
        RequirementListItemDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            title?: string;
            description?: string | null;
            externalKey?: string | null;
            priority?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: int32 */
            caseCount?: number;
            /** Format: int32 */
            passedCaseCount?: number;
            createdByName?: string | null;
            /** Format: date-time */
            planStartDate?: string | null;
            /** Format: date-time */
            planEndDate?: string | null;
            /** Format: date-time */
            actualStartDate?: string | null;
            /** Format: date-time */
            actualEndDate?: string | null;
            status?: components["schemas"]["RequirementStatus"];
            /** Format: int32 */
            linkedPlanCount?: number;
        };
        RequirementListItemDtoPagedResult: {
            items?: components["schemas"]["RequirementListItemDto"][];
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
        };
        RequirementPlanRefDto: {
            /** Format: uuid */
            planId?: string;
            planName?: string;
            releaseName?: string | null;
            status?: components["schemas"]["TestPlanStatus"];
            /** Format: date-time */
            lastRoundAt?: string | null;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        RequirementStatus: 0 | 1 | 2;
        ResetPasswordRequest: {
            newPassword?: string;
        };
        ReviewActionRequest: {
            action?: string;
            note?: string | null;
        };
        RoleMatrixDto: {
            role?: string;
            roleName?: string;
            /** Format: int32 */
            permissions?: number;
            permissionNames?: string[];
            matrix?: {
                [key: string]: boolean;
            };
        };
        RunSuiteRequest: {
            /** Format: uuid */
            environmentId?: string | null;
            browsers?: string[] | null;
            variables?: {
                [key: string]: string;
            } | null;
            expandDataSets?: boolean;
        };
        SaveRecorderRequest: {
            name?: string | null;
            module?: string | null;
            priority?: string | null;
            baseUrl?: string | null;
            description?: string | null;
        };
        ScheduleDetailDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            name?: string;
            cronExpression?: string;
            enabled?: boolean;
            module?: string | null;
            priority?: string | null;
            testCaseIds?: string[];
            /** Format: uuid */
            environmentId?: string | null;
            /** Format: date-time */
            lastRunAt?: string | null;
            /** Format: date-time */
            nextRunAt?: string | null;
            /** Format: int32 */
            lastCreatedCount?: number;
            lastError?: string | null;
            cronDescription?: string;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string;
            browsers?: string[] | null;
            expandDataSets?: boolean;
            scopeKind?: components["schemas"]["ScheduleScopeKind"];
            testPlanIds?: string[] | null;
        };
        ScheduleHealth: {
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            enabled?: number;
            /** Format: int32 */
            withError?: number;
            /** Format: date-time */
            lastRunAt?: string | null;
            /** Format: date-time */
            nextRunAt?: string | null;
            lastErrorScheduleName?: string | null;
        };
        SchedulePlanRefDto: {
            /** Format: uuid */
            id?: string;
            name?: string;
            releaseName?: string | null;
            status?: components["schemas"]["TestPlanStatus"];
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        ScheduleScopeKind: 0 | 1;
        ScheduleSummaryDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            name?: string;
            cronExpression?: string;
            enabled?: boolean;
            module?: string | null;
            priority?: string | null;
            /** Format: int32 */
            testCaseCount?: number;
            /** Format: uuid */
            environmentId?: string | null;
            environmentName?: string | null;
            /** Format: date-time */
            lastRunAt?: string | null;
            /** Format: date-time */
            nextRunAt?: string | null;
            /** Format: int32 */
            lastCreatedCount?: number;
            lastError?: string | null;
            cronDescription?: string;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string;
            browsers?: string[] | null;
            expandDataSets?: boolean;
            scopeKind?: components["schemas"]["ScheduleScopeKind"];
            testPlans?: components["schemas"]["SchedulePlanRefDto"][] | null;
            createdByName?: string | null;
        };
        ScheduleSummaryDtoPagedResult: {
            items?: components["schemas"]["ScheduleSummaryDto"][];
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
        };
        ScriptExportDto: {
            /** Format: uuid */
            testCaseId?: string;
            name?: string;
            language?: string;
            script?: string;
        };
        SelectorConfig: {
            type?: string;
            description?: string | null;
            value?: string | null;
        };
        SetFlakeRequest: {
            isFlaky?: boolean;
        };
        SetPlanItemsRequest: {
            testCaseIds?: string[];
        };
        SetPlanStatusRequest: {
            status?: components["schemas"]["TestPlanStatus"];
        };
        SetSuiteCasesRequest: {
            cases?: components["schemas"]["SuiteCaseSpec"][] | null;
        };
        SettingsView: {
            aiBaseUrl?: string;
            aiApiKeyMasked?: string;
            hasAiApiKey?: boolean;
            aiModel?: string;
            /** Format: int32 */
            aiMaxTokens?: number;
            webhookTokenMasked?: string;
            hasWebhookToken?: boolean;
            /** Format: date-time */
            updatedAt?: string;
            allowPrivateNetworkImport?: boolean;
            notifyEnabled?: boolean;
            notifyOnFailureOnly?: boolean;
            notifyPlanResultEmail?: boolean;
            wecom?: components["schemas"]["NotifyChannelView"];
            dingtalk?: components["schemas"]["NotifyChannelView"];
            feishu?: components["schemas"]["NotifyChannelView"];
            smtpHost?: string;
            /** Format: int32 */
            smtpPort?: number;
            smtpUseSsl?: boolean;
            smtpUser?: string;
            smtpPasswordMasked?: string;
            hasSmtpPassword?: boolean;
            mailTo?: string;
            ssoAutoProvision?: boolean;
            ssoFrontendBaseUrl?: string;
            ssoWecomEnabled?: boolean;
            ssoWecomCorpId?: string;
            ssoWecomAgentId?: string;
            ssoWecomSecretMasked?: string;
            hasSsoWecomSecret?: boolean;
            ssoDingtalkEnabled?: boolean;
            ssoDingtalkClientId?: string;
            ssoDingtalkClientSecretMasked?: string;
            hasSsoDingtalkClientSecret?: boolean;
            ssoOidcEnabled?: boolean;
            ssoOidcAuthority?: string;
            ssoOidcClientId?: string;
            ssoOidcClientSecretMasked?: string;
            hasSsoOidcClientSecret?: boolean;
            ssoOidcDisplayName?: string;
            ssoOidcScopes?: string;
            agentLoopEnabled?: boolean;
        };
        ShareDto: {
            /** Format: uuid */
            id?: string;
            token?: string;
            kind?: components["schemas"]["ReportShareKind"];
            /** Format: uuid */
            refId?: string;
            /** Format: uuid */
            projectId?: string | null;
            title?: string;
            /** Format: date-time */
            expiresAt?: string | null;
            revoked?: boolean;
            /** Format: int32 */
            viewCount?: number;
            /** Format: date-time */
            lastViewedAt?: string | null;
            /** Format: date-time */
            createdAt?: string;
            url?: string;
        };
        SharedStepGroup: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            project?: components["schemas"]["Project"];
            name?: string;
            description?: string | null;
            items?: components["schemas"]["SharedStepItem"][];
            variables?: components["schemas"]["SharedVariableEntry"][];
            /** Format: uuid */
            createdById?: string | null;
            /** Format: uuid */
            updatedById?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string | null;
        };
        SharedStepGroupDetail: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            name?: string;
            description?: string | null;
            items?: components["schemas"]["SharedStepItemDto"][];
            variables?: components["schemas"]["SharedVariableDto"][];
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string | null;
        };
        SharedStepGroupRequest: {
            /** Format: uuid */
            projectId?: string;
            name?: string;
            description?: string | null;
            items?: components["schemas"]["SharedStepItemDto"][] | null;
            variables?: components["schemas"]["SharedVariableDto"][] | null;
        };
        SharedStepGroupView: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            name?: string;
            description?: string | null;
            /** Format: int32 */
            itemCount?: number;
            /** Format: int32 */
            usedByCaseCount?: number;
            variables?: components["schemas"]["SharedVariableDto"][];
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string | null;
            createdByName?: string | null;
        };
        SharedStepGroupViewPagedResult: {
            items?: components["schemas"]["SharedStepGroupView"][];
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
        };
        SharedStepItem: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            groupId?: string;
            group?: components["schemas"]["SharedStepGroup"];
            /** Format: int32 */
            stepOrder?: number;
            actionType?: components["schemas"]["ActionType"];
            config?: components["schemas"]["StepConfig"];
            aiInstruction?: string | null;
            aiElementDescription?: string | null;
        };
        SharedStepItemDto: {
            /** Format: int32 */
            stepOrder?: number;
            actionType?: components["schemas"]["ActionType"];
            config?: components["schemas"]["StepConfig"];
            aiInstruction?: string | null;
            aiElementDescription?: string | null;
        };
        SharedStepOption: {
            /** Format: uuid */
            id?: string;
            name?: string;
            /** Format: int32 */
            itemCount?: number;
        };
        SharedStepUsageDto: {
            /** Format: uuid */
            testCaseId?: string;
            name?: string;
            stepOrders?: number[];
        };
        SharedVariableDto: {
            name?: string;
            value?: string | null;
        };
        SharedVariableEntry: {
            name?: string;
            value?: string;
        };
        SsoBindRequest: {
            code?: string;
            state?: string;
        };
        SsoLoginRequest: {
            code?: string;
            state?: string;
        };
        SsoProviderInfo: {
            id?: string;
            displayName?: string;
            authorizeUrl?: string;
        };
        StartRoundRequest: {
            /** Format: uuid */
            environmentId?: string | null;
            browsers?: string[] | null;
            expandDataSets?: boolean | null;
            variables?: {
                [key: string]: string;
            } | null;
        };
        StepConfig: {
            url?: string | null;
            selector?: components["schemas"]["SelectorConfig"];
            method?: string | null;
            endpoint?: string | null;
            headers?: components["schemas"]["HeaderEntry"][] | null;
            body?: string | null;
            value?: string | null;
            attribute?: string | null;
        };
        SuiteCaseItemDto: {
            /** Format: uuid */
            testCaseId?: string;
            name?: string;
            module?: string | null;
            priority?: string | null;
            type?: components["schemas"]["TestType"];
            isFlaky?: boolean;
            visualEnabled?: boolean;
            /** Format: uuid */
            dataSetId?: string | null;
            /** Format: int32 */
            dataRowCount?: number;
            /** Format: int32 */
            order?: number;
            /** Format: uuid */
            dependsOnTestCaseId?: string | null;
            dependsOnName?: string | null;
        };
        SuiteCaseSpec: {
            /** Format: uuid */
            testCaseId?: string;
            /** Format: uuid */
            dependsOnTestCaseId?: string | null;
        };
        SuiteDetailDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            name?: string;
            description?: string | null;
            kind?: components["schemas"]["SuiteKind"];
            /** Format: uuid */
            environmentId?: string | null;
            environmentName?: string | null;
            cases?: components["schemas"]["SuiteCaseItemDto"][];
            /** Format: date-time */
            lastRunAt?: string | null;
            /** Format: uuid */
            lastSuiteRunId?: string | null;
            /** Format: int32 */
            lastCreatedCount?: number;
            lastError?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string;
            failurePolicy?: components["schemas"]["SuiteFailurePolicy"];
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        SuiteFailurePolicy: 0 | 1;
        /**
         * Format: int32
         * @enum {integer}
         */
        SuiteKind: 0 | 1 | 2 | 3;
        SuiteRunSummaryDto: {
            /** Format: uuid */
            suiteRunId?: string;
            /** Format: uuid */
            suiteId?: string;
            /** Format: date-time */
            startedAt?: string;
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            passed?: number;
            /** Format: int32 */
            failed?: number;
            /** Format: int32 */
            error?: number;
            /** Format: int32 */
            skipped?: number;
            /** Format: int32 */
            pending?: number;
            /** Format: double */
            passRate?: number;
            /** Format: int32 */
            durationMs?: number;
            triggerSource?: string | null;
            /** Format: int32 */
            orchestrationSkipped?: number;
        };
        SuiteSummaryDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            name?: string;
            description?: string | null;
            kind?: components["schemas"]["SuiteKind"];
            /** Format: int32 */
            caseCount?: number;
            /** Format: uuid */
            environmentId?: string | null;
            environmentName?: string | null;
            /** Format: date-time */
            lastRunAt?: string | null;
            /** Format: uuid */
            lastSuiteRunId?: string | null;
            /** Format: int32 */
            lastCreatedCount?: number;
            lastError?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string;
            failurePolicy?: components["schemas"]["SuiteFailurePolicy"];
            createdByName?: string | null;
        };
        SuiteSummaryDtoPagedResult: {
            items?: components["schemas"]["SuiteSummaryDto"][];
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
        };
        TestCase: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            project?: components["schemas"]["Project"];
            name?: string;
            type?: components["schemas"]["TestType"];
            description?: string | null;
            caseCode?: string | null;
            module?: string | null;
            sourceSteps?: string | null;
            expectedResult?: string | null;
            networkRules?: string | null;
            aiGenerated?: boolean;
            aiPrompt?: string | null;
            browser?: string | null;
            /** Format: int32 */
            timeout?: number;
            /** Format: int32 */
            retryCount?: number;
            failFast?: boolean;
            isFlaky?: boolean;
            /** Format: double */
            flakeRate?: number;
            /** Format: date-time */
            flakeCheckedAt?: string | null;
            /** Format: uuid */
            dataSetId?: string | null;
            dataSet?: components["schemas"]["DataSet"];
            visualEnabled?: boolean;
            /** Format: double */
            visualThreshold?: number;
            visualIgnoreRegions?: string | null;
            customFields?: string | null;
            reviewStatus?: components["schemas"]["CaseReviewStatus"];
            /** Format: uuid */
            reviewSubmittedById?: string | null;
            /** Format: date-time */
            reviewSubmittedAt?: string | null;
            /** Format: uuid */
            reviewedById?: string | null;
            reviewedBy?: components["schemas"]["User"];
            /** Format: date-time */
            reviewedAt?: string | null;
            reviewNote?: string | null;
            /** Format: uuid */
            requirementId?: string | null;
            requirement?: components["schemas"]["Requirement"];
            /** Format: int32 */
            version?: number;
            /** Format: uuid */
            parentId?: string | null;
            parent?: components["schemas"]["TestCase"];
            status?: components["schemas"]["TestCaseStatus"];
            priority?: string | null;
            baseUrl?: string | null;
            /** Format: date-time */
            deletedAt?: string | null;
            steps?: components["schemas"]["TestStep"][];
            executions?: components["schemas"]["Execution"][];
            /** Format: uuid */
            createdById?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: uuid */
            updatedById?: string | null;
            /** Format: date-time */
            updatedAt?: string;
        };
        TestCaseDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            name?: string;
            type?: components["schemas"]["TestType"];
            description?: string | null;
            aiGenerated?: boolean;
            browser?: string | null;
            /** Format: int32 */
            timeout?: number;
            /** Format: int32 */
            retryCount?: number;
            /** Format: int32 */
            version?: number;
            status?: components["schemas"]["TestCaseStatus"];
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string;
            steps?: components["schemas"]["TestStepDto"][];
            baseUrl?: string | null;
            failFast?: boolean;
            caseCode?: string | null;
            module?: string | null;
            sourceSteps?: string | null;
            expectedResult?: string | null;
            priority?: string | null;
            isFlaky?: boolean;
            /** Format: double */
            flakeRate?: number;
            /** Format: date-time */
            flakeCheckedAt?: string | null;
            visualEnabled?: boolean;
            /** Format: double */
            visualThreshold?: number;
            visualIgnoreRegions?: string | null;
            customFields?: string | null;
            /** Format: uuid */
            dataSetId?: string | null;
            reviewStatus?: components["schemas"]["CaseReviewStatus"];
            /** Format: date-time */
            reviewedAt?: string | null;
            reviewNote?: string | null;
            reviewedByName?: string | null;
            /** Format: uuid */
            requirementId?: string | null;
            requirementTitle?: string | null;
            projectName?: string | null;
            networkRules?: string | null;
        };
        TestCaseModuleStatDto: {
            module?: string;
            /** Format: int32 */
            count?: number;
        };
        TestCaseSnapshot: {
            name?: string;
            description?: string | null;
            type?: components["schemas"]["TestType"];
            caseCode?: string | null;
            module?: string | null;
            priority?: string | null;
            browser?: string | null;
            /** Format: int32 */
            timeout?: number;
            /** Format: int32 */
            retryCount?: number;
            failFast?: boolean;
            baseUrl?: string | null;
            expectedResult?: string | null;
            sourceSteps?: string | null;
            visualEnabled?: boolean;
            /** Format: double */
            visualThreshold?: number;
            visualIgnoreRegions?: string | null;
            customFields?: string | null;
            reviewStatus?: components["schemas"]["CaseReviewStatus"];
            /** Format: uuid */
            dataSetId?: string | null;
            /** Format: uuid */
            requirementId?: string | null;
            steps?: components["schemas"]["TestStepSnapshot"][];
            networkRules?: string | null;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        TestCaseStatus: 0 | 1;
        TestCaseSummaryDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            name?: string;
            type?: components["schemas"]["TestType"];
            description?: string | null;
            aiGenerated?: boolean;
            browser?: string | null;
            /** Format: int32 */
            timeout?: number;
            /** Format: int32 */
            retryCount?: number;
            /** Format: int32 */
            version?: number;
            status?: components["schemas"]["TestCaseStatus"];
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string;
            baseUrl?: string | null;
            failFast?: boolean;
            caseCode?: string | null;
            module?: string | null;
            priority?: string | null;
            isFlaky?: boolean;
            /** Format: double */
            flakeRate?: number;
            visualEnabled?: boolean;
            /** Format: double */
            visualThreshold?: number;
            visualIgnoreRegions?: string | null;
            customFields?: string | null;
            /** Format: uuid */
            dataSetId?: string | null;
            reviewStatus?: components["schemas"]["CaseReviewStatus"];
            /** Format: date-time */
            reviewedAt?: string | null;
            reviewNote?: string | null;
            reviewedByName?: string | null;
            /** Format: uuid */
            requirementId?: string | null;
            requirementTitle?: string | null;
            projectName?: string | null;
            latestExecutionStatus?: components["schemas"]["ExecutionStatus"];
            /** Format: date-time */
            lastExecutedAt?: string | null;
            createdByName?: string | null;
        };
        TestCaseSummaryDtoPagedResult: {
            items?: components["schemas"]["TestCaseSummaryDto"][];
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
        };
        TestCaseVersionDetailDto: {
            /** Format: int32 */
            version?: number;
            /** Format: date-time */
            createdAt?: string;
            operatorName?: string | null;
            changeSummary?: string | null;
            snapshot?: components["schemas"]["TestCaseSnapshot"];
        };
        TestCaseVersionSummaryDto: {
            /** Format: int32 */
            version?: number;
            /** Format: date-time */
            createdAt?: string;
            operatorName?: string | null;
            changeSummary?: string | null;
            /** Format: int32 */
            stepCount?: number;
        };
        TestMailRequest: {
            to?: string | null;
            /** Format: uuid */
            planId?: string | null;
        };
        TestPlan: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            project?: components["schemas"]["Project"];
            name?: string;
            description?: string | null;
            releaseName?: string | null;
            status?: components["schemas"]["TestPlanStatus"];
            /** Format: date-time */
            startsAt?: string | null;
            /** Format: date-time */
            endsAt?: string | null;
            /** Format: uuid */
            ownerId?: string | null;
            owner?: components["schemas"]["User"];
            /** Format: uuid */
            requirementId?: string | null;
            requirement?: components["schemas"]["Requirement"];
            /** Format: double */
            targetPassRate?: number;
            allowErrors?: boolean;
            excludeFlakyFromFailure?: boolean;
            gateMode?: components["schemas"]["PlanGateMode"];
            defectGateEnabled?: boolean;
            /** Format: uuid */
            environmentId?: string | null;
            environment?: components["schemas"]["Environment"];
            browsers?: string[] | null;
            expandDataSets?: boolean;
            /** Format: date-time */
            lastRoundAt?: string | null;
            /** Format: int32 */
            lastCreatedCount?: number;
            lastError?: string | null;
            items?: components["schemas"]["TestPlanItem"][];
            rounds?: components["schemas"]["TestPlanRound"][];
            /** Format: uuid */
            createdById?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: uuid */
            updatedById?: string | null;
            /** Format: date-time */
            updatedAt?: string | null;
        };
        TestPlanDetailDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            projectName?: string | null;
            name?: string;
            description?: string | null;
            releaseName?: string | null;
            status?: components["schemas"]["TestPlanStatus"];
            /** Format: date-time */
            startsAt?: string | null;
            /** Format: date-time */
            endsAt?: string | null;
            /** Format: uuid */
            ownerId?: string | null;
            ownerName?: string | null;
            /** Format: double */
            targetPassRate?: number;
            allowErrors?: boolean;
            excludeFlakyFromFailure?: boolean;
            gateMode?: components["schemas"]["PlanGateMode"];
            defectGateEnabled?: boolean;
            /** Format: uuid */
            environmentId?: string | null;
            environmentName?: string | null;
            browsers?: string[];
            expandDataSets?: boolean;
            /** Format: int32 */
            caseCount?: number;
            /** Format: int32 */
            roundCount?: number;
            schedules?: components["schemas"]["PlanScheduleRefDto"][] | null;
            scopeIssues?: components["schemas"]["PlanScopeIssueDto"][];
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string | null;
            /** Format: uuid */
            requirementId?: string | null;
            requirementTitle?: string | null;
        };
        TestPlanItem: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            planId?: string;
            plan?: components["schemas"]["TestPlan"];
            /** Format: uuid */
            testCaseId?: string;
            testCase?: components["schemas"]["TestCase"];
            /** Format: int32 */
            order?: number;
        };
        TestPlanItemDto: {
            /** Format: uuid */
            testCaseId?: string;
            name?: string;
            module?: string | null;
            priority?: string | null;
            type?: components["schemas"]["TestType"];
            status?: components["schemas"]["TestCaseStatus"];
            isFlaky?: boolean;
            /** Format: int32 */
            order?: number;
            deleted?: boolean;
        };
        TestPlanReportDto: {
            plan?: components["schemas"]["TestPlanSummaryDto"];
            gate?: components["schemas"]["PlanGateResult"];
            trends?: components["schemas"]["PlanRoundTrendDto"][];
            modules?: components["schemas"]["PlanModuleStatDto"][];
            blockingCases?: components["schemas"]["PlanBlockingCaseDto"][];
        };
        TestPlanRound: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            planId?: string;
            plan?: components["schemas"]["TestPlan"];
            /** Format: int32 */
            roundNo?: number;
            status?: components["schemas"]["PlanRoundStatus"];
            triggerType?: components["schemas"]["TriggerType"];
            triggerSource?: string | null;
            /** Format: uuid */
            triggeredById?: string | null;
            /** Format: uuid */
            environmentId?: string | null;
            browsers?: string[] | null;
            expandDataSets?: boolean;
            /** Format: int32 */
            createdCount?: number;
            /** Format: date-time */
            startedAt?: string;
            /** Format: date-time */
            completedAt?: string | null;
            error?: string | null;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        TestPlanStatus: 0 | 1 | 2 | 3;
        TestPlanSummaryDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            projectId?: string;
            projectName?: string | null;
            name?: string;
            description?: string | null;
            releaseName?: string | null;
            status?: components["schemas"]["TestPlanStatus"];
            /** Format: date-time */
            startsAt?: string | null;
            /** Format: date-time */
            endsAt?: string | null;
            /** Format: uuid */
            ownerId?: string | null;
            ownerName?: string | null;
            /** Format: double */
            targetPassRate?: number;
            allowErrors?: boolean;
            excludeFlakyFromFailure?: boolean;
            gateMode?: components["schemas"]["PlanGateMode"];
            defectGateEnabled?: boolean;
            /** Format: uuid */
            environmentId?: string | null;
            /** Format: int32 */
            caseCount?: number;
            /** Format: int32 */
            runningRoundNo?: number | null;
            /** Format: int32 */
            runningCaseCount?: number | null;
            /** Format: int32 */
            runningPassedCount?: number | null;
            /** Format: date-time */
            lastRoundAt?: string | null;
            /** Format: int32 */
            lastRoundNo?: number | null;
            /** Format: double */
            lastPassRate?: number | null;
            lastError?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string | null;
            createdByName?: string | null;
            /** Format: uuid */
            requirementId?: string | null;
            requirementTitle?: string | null;
        };
        TestPlanSummaryDtoPagedResult: {
            items?: components["schemas"]["TestPlanSummaryDto"][];
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
        };
        TestStep: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            testCaseId?: string;
            testCase?: components["schemas"]["TestCase"];
            /** Format: int32 */
            stepOrder?: number;
            actionType?: components["schemas"]["ActionType"];
            config?: components["schemas"]["StepConfig"];
            aiInstruction?: string | null;
            aiElementDescription?: string | null;
            /** Format: uuid */
            sharedGroupId?: string | null;
            sharedGroup?: components["schemas"]["SharedStepGroup"];
            sharedVariables?: components["schemas"]["SharedVariableEntry"][] | null;
            /** Format: date-time */
            createdAt?: string;
        };
        TestStepDto: {
            /** Format: uuid */
            id?: string;
            /** Format: int32 */
            stepOrder?: number;
            actionType?: components["schemas"]["ActionType"];
            config?: components["schemas"]["StepConfig"];
            aiInstruction?: string | null;
            aiElementDescription?: string | null;
            /** Format: uuid */
            sharedGroupId?: string | null;
            sharedGroupName?: string | null;
            sharedVariables?: components["schemas"]["SharedVariableEntry"][] | null;
        };
        TestStepSnapshot: {
            /** Format: int32 */
            stepOrder?: number;
            actionType?: components["schemas"]["ActionType"];
            config?: string;
            aiInstruction?: string | null;
            aiElementDescription?: string | null;
            /** Format: uuid */
            sharedGroupId?: string | null;
            sharedVariables?: components["schemas"]["SharedVariableEntry"][] | null;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        TestType: 0 | 1 | 2;
        TrendPoint: {
            date?: string;
            /** Format: int32 */
            passed?: number;
            /** Format: int32 */
            failed?: number;
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            defectsCreated?: number;
            /** Format: int32 */
            defectsClosed?: number;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        TriggerType: 0 | 1 | 2 | 3 | 4;
        UnstableCaseItem: {
            /** Format: uuid */
            testCaseId?: string;
            testCaseName?: string;
            module?: string | null;
            /** Format: int32 */
            totalRuns?: number;
            /** Format: int32 */
            failCount?: number;
            /** Format: double */
            failRate?: number;
            /** Format: date-time */
            lastFailedAt?: string;
            isFlaky?: boolean;
        };
        UpdateDataSetRequest: {
            name?: string;
            description?: string | null;
            columns?: string[];
            rows?: {
                [key: string]: string;
            }[];
            firstRowIsSample?: boolean;
        };
        UpdateDefectRequest: {
            title?: string;
            description?: string | null;
            severity?: components["schemas"]["DefectSeverity"];
            /** Format: uuid */
            assignedToId?: string | null;
            externalRef?: string | null;
            testCaseIds?: string[] | null;
        };
        UpdateEnvironmentRequest: {
            name?: string;
            baseUrl?: string;
            loginUrl?: string | null;
            loginUsername?: string | null;
            loginPassword?: string | null;
            loginSuccessIndicator?: string | null;
            autoLogin?: boolean;
            browser?: string | null;
        };
        UpdateLoadTestScenarioRequest: {
            name?: string;
            description?: string | null;
            /** Format: uuid */
            environmentId?: string | null;
            targetBaseUrl?: string | null;
            /** Format: uuid */
            apiDefinitionId?: string | null;
            operations?: string[] | null;
            profile?: components["schemas"]["LoadTestProfile"];
            thresholds?: components["schemas"]["LoadTestThreshold"][] | null;
            variables?: {
                [key: string]: string;
            } | null;
            caseIds?: string[] | null;
        };
        UpdateProjectMemberRoleRequest: {
            role?: components["schemas"]["ProjectRole"];
        };
        UpdateProjectRequest: {
            name?: string;
            description?: string | null;
            /** Format: uuid */
            managerId?: string | null;
            /** Format: uuid */
            testOwnerId?: string | null;
            /** Format: uuid */
            developerOwnerId?: string | null;
            agentLoopEnabled?: boolean | null;
            treatAgentHealedAsPass?: boolean | null;
        };
        UpdateRequirementRequest: {
            title?: string;
            description?: string | null;
            externalKey?: string | null;
            priority?: string | null;
            /** Format: date-time */
            planStartDate?: string | null;
            /** Format: date-time */
            planEndDate?: string | null;
            /** Format: date-time */
            actualStartDate?: string | null;
            /** Format: date-time */
            actualEndDate?: string | null;
            status?: components["schemas"]["RequirementStatus"];
        };
        UpdateScheduleRequest: {
            name?: string;
            cronExpression?: string;
            enabled?: boolean;
            module?: string | null;
            priority?: string | null;
            testCaseIds?: string[] | null;
            /** Format: uuid */
            environmentId?: string | null;
            browsers?: string[] | null;
            expandDataSets?: boolean;
            scopeKind?: components["schemas"]["ScheduleScopeKind"];
            testPlanIds?: string[] | null;
        };
        UpdateSettingsRequest: {
            aiBaseUrl?: string | null;
            aiApiKey?: string | null;
            aiModel?: string | null;
            /** Format: int32 */
            aiMaxTokens?: number | null;
            webhookToken?: string | null;
            allowPrivateNetworkImport?: boolean | null;
            notifyEnabled?: boolean | null;
            notifyOnFailureOnly?: boolean | null;
            notifyPlanResultEmail?: boolean | null;
            notifyWecomWebhook?: string | null;
            notifyDingtalkWebhook?: string | null;
            notifyFeishuWebhook?: string | null;
            smtpHost?: string | null;
            /** Format: int32 */
            smtpPort?: number | null;
            smtpUseSsl?: boolean | null;
            smtpUser?: string;
            smtpPassword?: string | null;
            mailTo?: string | null;
            ssoAutoProvision?: boolean | null;
            ssoFrontendBaseUrl?: string | null;
            ssoWecomEnabled?: boolean | null;
            ssoWecomCorpId?: string | null;
            ssoWecomAgentId?: string | null;
            ssoWecomSecret?: string | null;
            ssoDingtalkEnabled?: boolean | null;
            ssoDingtalkClientId?: string | null;
            ssoDingtalkClientSecret?: string | null;
            ssoOidcEnabled?: boolean | null;
            ssoOidcAuthority?: string | null;
            ssoOidcClientId?: string | null;
            ssoOidcClientSecret?: string | null;
            ssoOidcDisplayName?: string | null;
            ssoOidcScopes?: string | null;
            clearWebhook?: string | null;
            agentLoopEnabled?: boolean | null;
        };
        UpdateSuiteRequest: {
            name?: string;
            description?: string | null;
            kind?: components["schemas"]["SuiteKind"];
            /** Format: uuid */
            environmentId?: string | null;
            cases?: components["schemas"]["SuiteCaseSpec"][] | null;
            failurePolicy?: components["schemas"]["SuiteFailurePolicy"];
        };
        UpdateTestCaseRequest: {
            name?: string;
            description?: string | null;
            status?: components["schemas"]["TestCaseStatus"];
            browser?: string | null;
            /** Format: int32 */
            timeout?: number;
            /** Format: int32 */
            retryCount?: number;
            baseUrl?: string | null;
            failFast?: boolean;
            caseCode?: string | null;
            module?: string | null;
            sourceSteps?: string | null;
            expectedResult?: string | null;
            priority?: string | null;
            visualEnabled?: boolean;
            /** Format: double */
            visualThreshold?: number | null;
            visualIgnoreRegions?: string | null;
            customFields?: string | null;
            /** Format: uuid */
            dataSetId?: string | null;
            reviewStatus?: components["schemas"]["CaseReviewStatus"];
            /** Format: date-time */
            reviewedAt?: string | null;
            reviewNote?: string | null;
            reviewedByName?: string | null;
            /** Format: uuid */
            requirementId?: string | null;
            networkRules?: string | null;
        };
        UpdateTestCaseStepsRequest: {
            steps?: components["schemas"]["UpdateTestStepRequest"][];
        };
        UpdateTestPlanRequest: {
            name?: string;
            description?: string | null;
            releaseName?: string | null;
            /** Format: date-time */
            startsAt?: string | null;
            /** Format: date-time */
            endsAt?: string | null;
            /** Format: uuid */
            ownerId?: string | null;
            /** Format: double */
            targetPassRate?: number | null;
            allowErrors?: boolean | null;
            excludeFlakyFromFailure?: boolean | null;
            gateMode?: components["schemas"]["PlanGateMode"];
            defectGateEnabled?: boolean | null;
            /** Format: uuid */
            environmentId?: string | null;
            browsers?: string[] | null;
            expandDataSets?: boolean | null;
            /** Format: uuid */
            requirementId?: string | null;
        };
        UpdateTestStepRequest: {
            /** Format: int32 */
            stepOrder?: number;
            actionType?: components["schemas"]["ActionType"];
            config?: components["schemas"]["StepConfig"];
            aiInstruction?: string | null;
            aiElementDescription?: string | null;
            /** Format: uuid */
            sharedGroupId?: string | null;
            sharedVariables?: components["schemas"]["SharedVariableEntry"][] | null;
        };
        UpdateUserRequest: {
            displayName?: string;
            role?: components["schemas"]["UserRole"];
            isActive?: boolean;
            email?: string | null;
        };
        User: {
            /** Format: uuid */
            id?: string;
            username?: string;
            passwordHash?: string;
            displayName?: string;
            email?: string | null;
            /** Format: date-time */
            createdAt?: string;
            role?: components["schemas"]["UserRole"];
            isActive?: boolean;
            /** Format: int32 */
            tokenVersion?: number;
            /** Format: date-time */
            lastLoginAt?: string | null;
            lastLoginIp?: string | null;
            /** Format: uuid */
            createdById?: string | null;
            createdBy?: components["schemas"]["User"];
            ssoProvider?: string | null;
            ssoSubject?: string | null;
            /** Format: date-time */
            updatedAt?: string | null;
        };
        UserCandidateDto: {
            /** Format: uuid */
            id?: string;
            username?: string;
            displayName?: string | null;
        };
        UserDto: {
            /** Format: uuid */
            id?: string;
            username?: string;
            displayName?: string;
            role?: components["schemas"]["UserRole"];
            roleName?: string;
            /** Format: int32 */
            permissions?: number;
            permissionNames?: string[];
        };
        UserOptionDto: {
            /** Format: uuid */
            id?: string;
            name?: string;
            hasEmail?: boolean;
            isActive?: boolean;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        UserRole: 0 | 1 | 2 | 3;
        VisualBaselineDto: {
            /** Format: uuid */
            id?: string;
            /** Format: uuid */
            testCaseId?: string;
            testCaseName?: string;
            module?: string | null;
            /** Format: int32 */
            stepOrder?: number;
            imageUrl?: string;
            /** Format: int32 */
            width?: number;
            /** Format: int32 */
            height?: number;
            /** Format: int32 */
            compareCount?: number;
            /** Format: date-time */
            lastComparedAt?: string | null;
            /** Format: uuid */
            sourceExecutionId?: string | null;
            /** Format: date-time */
            createdAt?: string;
            /** Format: date-time */
            updatedAt?: string;
        };
        VisualBaselineDtoPagedResult: {
            items?: components["schemas"]["VisualBaselineDto"][];
            /** Format: int32 */
            total?: number;
            /** Format: int32 */
            page?: number;
            /** Format: int32 */
            pageSize?: number;
        };
        VisualCaseSettingDto: {
            /** Format: uuid */
            testCaseId?: string;
            name?: string;
            visualEnabled?: boolean;
            /** Format: double */
            visualThreshold?: number;
            /** Format: int32 */
            baselineCount?: number;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        VisualStatus: 0 | 1 | 2 | 3;
        WebhookTriggerRequest: {
            testCaseIds?: string[] | null;
            /** Format: uuid */
            projectId?: string | null;
            module?: string | null;
            priority?: string | null;
            /** Format: uuid */
            environmentId?: string | null;
            commitSha?: string | null;
            branch?: string | null;
            buildNumber?: string | null;
            source?: string | null;
            excludeFlaky?: boolean;
            browsers?: string[] | null;
            variables?: {
                [key: string]: string;
            } | null;
        };
    };
    responses: never;
    parameters: never;
    requestBodies: never;
    headers: never;
    pathItems: never;
}
export type $defs = Record<string, never>;
export type operations = Record<string, never>;

