// Shared type definitions for user management

export interface User {
  id: string;
  email: string;
  name: string;
  provider: string;
  providerUserId?: string;
  profileImageUrl?: string;
  createdAt: Date;
  updatedAt: Date;
}

export interface CreateUserRequest {
  email: string;
  name: string;
  provider: string;
  providerUserId?: string;
  profileImageUrl?: string;
}

export interface UpdateUserRequest {
  name?: string;
  profileImageUrl?: string;
}

export interface LoginRequest {
  email: string;
  password?: string;
  provider: string;
  providerToken?: string;
}

export interface LoginResponse {
  user: User;
  accessToken: string;
  refreshToken: string;
  expiresAt: Date;
}

export interface RefreshTokenRequest {
  refreshToken: string;
}

export interface RefreshTokenResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: Date;
}