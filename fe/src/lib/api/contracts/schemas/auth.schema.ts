import { z } from "zod";
import { userRoleSchema } from "./common.schema";

export const loginRequestSchema = z.object({
    email: z.email(),
    password: z.string().min(1),
})

export const registerRequestSchema = z.object({
    email: z.email(),
    password: z.string().min(1),
    firstName: z.string().min(1),
    lastName: z.string().min(1),
    phoneNumber: z.string().nullable(),
})

export const userDtoSchema = z.object({
    id: z.string(),
    email: z.string(),
    firstName: z.string(),
    lastName: z.string(),
    phoneNumber: z.string().nullable(),
    role: userRoleSchema,
})

export const authResponseSchema = z.object({
    token: z.string(),
    user: userDtoSchema,
})

export type LoginRequest = z.infer<typeof loginRequestSchema>;
export type RegisterRequest = z.infer<typeof registerRequestSchema>;

/** Mirrors backend/Shop.Application/Dtos/Dtos.cs UserDto. */
export type UserDto = z.infer<typeof userDtoSchema>;
export type AuthResponse = z.infer<typeof authResponseSchema>;