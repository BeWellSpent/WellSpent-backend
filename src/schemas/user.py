from pydantic import BaseModel, Field

class CreateUserRequest(BaseModel):
    id: int | None = Field(default=None, title="user identifier")
    username: str | None = Field(default=None, title="username of the user")
    email: str | None = Field(default=None, title="email address of the user")
    first_name: str | None = Field(default=None, title="first name of the user")
    last_name: str | None = Field(default=None, title="last name of the user")
    active: bool | None = Field(default=True, title="is the user active")