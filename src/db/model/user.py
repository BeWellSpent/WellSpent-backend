from sqlmodel import SQLModel, Field

class User(SQLModel, table=True):
    __tablename__ = "users"
    id: int | None = Field(default=None, primary_key=True)
    username: str | None = Field()
    email: str | None = Field()
    first_name: str | None = Field()
    last_name: str | None = Field()
    active: bool | None = Field()