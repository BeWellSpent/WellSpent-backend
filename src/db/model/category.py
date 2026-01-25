from sqlmodel import SQLModel, Field

class CategoryType(SQLModel, table=True):
    __tablename__ = "category_type"
    id: int | None = Field(default=None, primary_key=True)
    name: str | None = Field()

class Category(SQLModel, table=True):
    __tablename__ = "category"
    id: int | None = Field(default=None, primary_key=True)
    name: str | None = Field()
    type_id: int | None = Field(foreign_key="category_type.id")
    user_id: int | None = Field(foreign_key="users.id")