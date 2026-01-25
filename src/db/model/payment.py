from sqlmodel import SQLModel, Field

class PaymentTypeModel(SQLModel, table=True):
    __tablename__ = "payment_type"
    Id: int = Field(default=None, primary_key=True)
    name: str = Field(index=True)

class PaymentMethodModel(SQLModel, table=True):
    __tablename__ = "payment_method"
    Id: str = Field(default=None, primary_key=True)
    name: str = Field(index=True)
    payment_type_id: int | None = Field(foreign_key="payment_type.id")
    user_id: int | None = Field(foreign_key="users.id")